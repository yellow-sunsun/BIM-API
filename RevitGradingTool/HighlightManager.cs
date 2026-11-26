using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGradingTool
{
    public class HighlightManager
    {
        private const double MM = 1.0 / 304.8;
        // 放大錯誤時的剖面框（以錯誤為中心，跟著鏡頭方向旋轉）
        private const double FOCUS_SIDE = 4000 * MM;      // 左右各 4m
        private const double FOCUS_BACK = 4000 * MM;      // 錯誤後方（建築內側）4m
        private const double FOCUS_FRONT = 1000 * MM;     // 錯誤前方（鏡頭那側）只留 1m，避免外牆擋住
        private const double FOCUS_BELOW = 2500 * MM;     // 錯誤下方 2.5m
        private const double CAMERA_PITCH_DEG = 35;       // 鏡頭往下俯視的角度

        private readonly Document _doc;
        private readonly View _view;   // 標色所在的視圖（3D 視圖）
        private readonly List<ElementId> _highlightedIds = new List<ElementId>();
        private readonly List<ElementId> _markerIds = new List<ElementId>();
        // 每個錯誤在這個模型中對應的元件（標色的構件或標記方塊），放大時用來選取
        private readonly Dictionary<CompareError, ElementId> _errorElements = new Dictionary<CompareError, ElementId>();

        // 原本的剖面框與視角，清除時還原
        private bool _sectionBoxSaved;
        private bool _origSectionBoxActive;
        private BoundingBoxXYZ _origSectionBox;
        private ViewOrientation3D _origOrientation;

        public HighlightManager(Document doc, View view)
        {
            _doc = doc;
            _view = view ?? doc.ActiveView;
        }

        public Document Doc => _doc;

        // 合併的錯誤（例如一起被移動的牆和門）：組裡每個構件都要標色
        private static IEnumerable<CompareError> WithRelated(List<CompareError> errors)
        {
            foreach (var err in errors)
            {
                yield return err;
                if (err.Related == null) continue;
                foreach (var r in err.Related) yield return r;
            }
        }


        // 找預設的 {3D} 視圖，沒有的話建立一個
        public static View3D Get3DView(Document doc)
        {
            var views = new FilteredElementCollector(doc).OfClass(typeof(View3D)).Cast<View3D>()
                .Where(v => !v.IsTemplate && !v.IsPerspective)
                .OrderBy(v => v.Name == "{3D}" ? 0 : v.Name.StartsWith("{3D") ? 1 : 2)
                .ThenBy(v => v.Name)
                .ToList();
            if (views.Count > 0) return views[0];

            var vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>()
                .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
            if (vft == null) return null;
            using (var tx = new Transaction(doc, "\u5efa\u7acb 3D \u8996\u5716"))
            {
                tx.Start();
                var v = View3D.CreateIsometric(doc, vft.Id);
                tx.Commit();
                return v;
            }
        }

        public void ClearAll()
        {
            using (var tx = new Transaction(_doc, "\u6e05\u9664\u6a19\u8272"))
            {
                tx.Start();

                foreach (var id in _highlightedIds)
                {
                    if (_doc.GetElement(id) != null)
                        _view.SetElementOverrides(id, new OverrideGraphicSettings());
                }

                foreach (var id in _markerIds)
                {
                    if (_doc.GetElement(id) != null)
                        _doc.Delete(id);
                }

                if (_sectionBoxSaved && _view is View3D v3)
                {
                    if (_origSectionBox != null) v3.SetSectionBox(_origSectionBox);
                    v3.IsSectionBoxActive = _origSectionBoxActive;
                    if (_origOrientation != null && !v3.IsLocked) v3.SetOrientation(_origOrientation);
                    _sectionBoxSaved = false;
                }

                tx.Commit();
            }

            _highlightedIds.Clear();
            _markerIds.Clear();
            _errorElements.Clear();
        }

        // 標準模型標色（用標準座標）
        public void Highlight(List<CompareError> errors, ModelSnapshot standard)
        {
            ClearAll();

            using (var tx = new Transaction(_doc, "\u6a19\u8272\u5b78\u751f\u932f\u8aa4"))
            {
                tx.Start();
                foreach (var err in WithRelated(errors))
                {
                    XYZ position = new XYZ(err.X * MM, err.Y * MM, err.Z * MM);
                    if (err.Type == ErrorType.Extra)
                        AddMarker(err, position, "\u2b06 \u591a\u9918\u69cb\u4ef6", new Color(160, 32, 240));
                    else
                        ApplyColor(err, position, err.StdElementId);
                }
                tx.Commit();
            }
        }

        // 學生模型標色（用學生模型的原始座標，避免整棟偏移時標錯位置）
        public void HighlightStudent(List<CompareError> errors)
        {
            ClearAll();

            using (var tx = new Transaction(_doc, "\u6a19\u8272\u5b78\u751f\u932f\u8aa4"))
            {
                tx.Start();
                foreach (var err in WithRelated(errors))
                {
                    XYZ position = new XYZ(err.RawStudentX * MM, err.RawStudentY * MM, err.RawStudentZ * MM);
                    if (err.Type == ErrorType.Missing)
                        AddMarker(err, position, "\u2b07 \u6b64\u8655\u7f3a\u5c11\u69cb\u4ef6", new Color(255, 0, 0));
                    else
                        ApplyColor(err, position, err.StuElementId);
                }
                tx.Commit();
            }
        }

        // 剖面框頂部要切在錯誤上方多少：梁被上方樓板蓋住，要切得很低；樓板本身薄
        private static double TopCut(string category)
        {
            if (category == "\u7d50\u69cb\u69cb\u67b6") return 150 * MM;
            if (category == "\u6a13\u677f") return 600 * MM;
            return 1000 * MM;
        }

        // 放大到某個錯誤：
        // 1. 鏡頭從建築外側、往下俯視 35° 對準錯誤，錯誤位於畫面正中央
        // 2. 剖面框跟著鏡頭方向旋轉，鏡頭那一側和錯誤上方切得很近，外牆、上層樓板不會擋住
        // 3. 視窗放大到剖面框範圍，並選取該構件
        public void FocusOn(CompareError err, XYZ centerMm, XYZ buildingCenterMm, bool select)
        {
            var v3 = _view as View3D;
            XYZ c = new XYZ(centerMm.X * MM, centerMm.Y * MM, centerMm.Z * MM);

            // 「往外」方向：從建築中心指向錯誤（水平），鏡頭放在這一側往內看
            XYZ outward = new XYZ(c.X - buildingCenterMm.X * MM, c.Y - buildingCenterMm.Y * MM, 0);
            outward = outward.GetLength() < 1e-6 ? new XYZ(1, -1, 0).Normalize() : outward.Normalize();
            XYZ right = new XYZ(outward.Y, -outward.X, 0);   // right x outward = Z（右手座標）
            double top = TopCut(err.Category);

            XYZ min = c - right * FOCUS_SIDE - outward * FOCUS_BACK - XYZ.BasisZ * FOCUS_BELOW;
            XYZ max = c + right * FOCUS_SIDE + outward * FOCUS_FRONT + XYZ.BasisZ * top;

            if (v3 != null)
            {
                using (var tx = new Transaction(_doc, "\u653e\u5927\u5230\u932f\u8aa4"))
                {
                    tx.Start();
                    if (!_sectionBoxSaved)
                    {
                        _origSectionBoxActive = v3.IsSectionBoxActive;
                        _origSectionBox = v3.GetSectionBox();
                        _origOrientation = v3.GetOrientation();
                        _sectionBoxSaved = true;
                    }

                    var t = Transform.Identity;
                    t.Origin = c;
                    t.BasisX = right;
                    t.BasisY = outward;
                    t.BasisZ = XYZ.BasisZ;
                    v3.SetSectionBox(new BoundingBoxXYZ
                    {
                        Transform = t,
                        Min = new XYZ(-FOCUS_SIDE, -FOCUS_BACK, -FOCUS_BELOW),
                        Max = new XYZ(FOCUS_SIDE, FOCUS_FRONT, top)
                    });
                    v3.IsSectionBoxActive = true;

                    if (!v3.IsLocked)
                    {
                        double pitch = CAMERA_PITCH_DEG * Math.PI / 180.0;
                        XYZ forward = new XYZ(-outward.X * Math.Cos(pitch), -outward.Y * Math.Cos(pitch), -Math.Sin(pitch)).Normalize();
                        XYZ up = (XYZ.BasisZ - forward * forward.DotProduct(XYZ.BasisZ)).Normalize();
                        XYZ eye = c - forward * (30000 * MM);
                        v3.SetOrientation(new ViewOrientation3D(eye, up, forward));
                    }
                    tx.Commit();
                }
            }

            var uiDoc = new UIDocument(_doc);
            var uiView = uiDoc.GetOpenUIViews().FirstOrDefault(u => u.ViewId == _view.Id);
            if (uiView != null)
            {
                // 以錯誤為中心、放大到剖面框大小（四周留一點邊）
                double r = FOCUS_SIDE * 1.1;
                uiView.ZoomAndCenterRectangle(c - new XYZ(r, r, r), c + new XYZ(r, r, r));
            }

            if (select && _errorElements.TryGetValue(err, out ElementId id) && _doc.GetElement(id) != null)
                uiDoc.Selection.SetElementIds(new List<ElementId> { id });
        }

        // 在 3D 視圖放一個半透明方塊當標記（3D 視圖不能放文字註記）；平面圖則放文字
        private void AddMarker(CompareError err, XYZ position, string label, Color color)
        {
            ElementId id;
            if (_view is View3D)
            {
                double half = 400 * MM;
                var p0 = new XYZ(position.X - half, position.Y - half, position.Z - half);
                var loop = new CurveLoop();
                loop.Append(Line.CreateBound(p0, p0 + new XYZ(2 * half, 0, 0)));
                loop.Append(Line.CreateBound(p0 + new XYZ(2 * half, 0, 0), p0 + new XYZ(2 * half, 2 * half, 0)));
                loop.Append(Line.CreateBound(p0 + new XYZ(2 * half, 2 * half, 0), p0 + new XYZ(0, 2 * half, 0)));
                loop.Append(Line.CreateBound(p0 + new XYZ(0, 2 * half, 0), p0));
                var solid = GeometryCreationUtilities.CreateExtrusionGeometry(
                    new List<CurveLoop> { loop }, XYZ.BasisZ, 2 * half);

                var ds = DirectShape.CreateElement(_doc, new ElementId(BuiltInCategory.OST_GenericModel));
                ds.SetShape(new GeometryObject[] { solid });
                ds.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS)?.Set(label);
                id = ds.Id;

                _view.SetElementOverrides(id, ColorOverride(color, 30));
            }
            else
            {
                var options = new TextNoteOptions
                {
                    HorizontalAlignment = HorizontalTextAlignment.Center,
                    TypeId = _doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType)
                };
                id = TextNote.Create(_doc, _view.Id, position, label, options).Id;
            }
            _markerIds.Add(id);
            _errorElements[err] = id;
            HighlightEvent.Log($"  [{_doc.Title}] \u653e\u6a19\u8a18\u65b9\u584a {err.Type} {err.Category} {err.FamilyType} \u2192 \u5143\u4ef6 {id}");
        }

        private void ApplyColor(CompareError err, XYZ position, long elementId)
        {
            // 優先用比對時記下的元件 Id；找不到時才用座標找最近的構件
            Element bestEl = elementId != 0 ? _doc.GetElement(new ElementId(elementId)) : null;
            if (bestEl == null || bestEl.Category?.Name != err.Category)
                bestEl = FindByPosition(err, position);
            if (bestEl == null)
            {
                HighlightEvent.Log($"  [{_doc.Title}] \u627e\u4e0d\u5230\u8981\u6a19\u8272\u7684\u5143\u4ef6\uff1a{err.Type} {err.Category} {err.FamilyType}\uff08Id {elementId}\uff09");
                return;
            }

            Color color = err.Type == ErrorType.Missing || err.Type == ErrorType.Extra ? new Color(255, 0, 0)
                        : err.Type == ErrorType.WrongRotation || err.Type == ErrorType.WrongPosition ? new Color(255, 255, 0)
                        : new Color(255, 140, 0);
            _view.SetElementOverrides(bestEl.Id, ColorOverride(color, 0));
            _highlightedIds.Add(bestEl.Id);
            _errorElements[err] = bestEl.Id;
            HighlightEvent.Log($"  [{_doc.Title}] \u6a19\u8272 {err.Type} {err.Category} {err.FamilyType} \u2192 \u5143\u4ef6 {bestEl.Id}");
        }

        // 標色設定：表面和「切面」都上色（被剖面框切到的構件，從切面看也要有顏色），線條也同色
        private OverrideGraphicSettings ColorOverride(Color color, int transparency)
        {
            var ovr = new OverrideGraphicSettings();
            ElementId solid = GetSolidFillId();
            ovr.SetSurfaceForegroundPatternColor(color);
            ovr.SetSurfaceForegroundPatternId(solid);
            ovr.SetCutForegroundPatternColor(color);
            ovr.SetCutForegroundPatternId(solid);
            ovr.SetCutLineColor(color);
            ovr.SetProjectionLineColor(color);
            if (transparency > 0) ovr.SetSurfaceTransparency(transparency);
            return ovr;
        }

        private Element FindByPosition(CompareError err, XYZ position)
        {
            double tol = (err.Category == "\u6a13\u677f" || err.Category == "\u7246")
                ? 500.0 * MM : 50.0 * MM;

            Element bestEl = null;
            double bestDist = double.MaxValue;

            foreach (Element el in new FilteredElementCollector(_doc).WhereElementIsNotElementType())
            {
                if (el.Category?.Name != err.Category) continue;

                XYZ elPt = GetElementCenter(el);
                if (elPt == null) continue;

                double dist;
                if (err.Category == "\u7d50\u69cb\u67f1")
                {
                    double xyDist = Math.Sqrt(
                        Math.Pow(elPt.X - position.X, 2) +
                        Math.Pow(elPt.Y - position.Y, 2));
                    if (xyDist > tol) continue;
                    dist = Math.Abs(elPt.Z - position.Z);
                }
                else
                {
                    dist = elPt.DistanceTo(position);
                    if (dist > tol) continue;
                }

                if (dist < bestDist) { bestDist = dist; bestEl = el; }
            }
            return bestEl;
        }

        private XYZ GetElementCenter(Element el)
        {
            if (el.Category?.Name == "\u7d50\u69cb\u67f1")
            {
                BoundingBoxXYZ bb = el.get_BoundingBox(null);
                if (bb != null)
                {
                    if (el.Location is LocationPoint lp)
                        return new XYZ(lp.Point.X, lp.Point.Y, (bb.Min.Z + bb.Max.Z) / 2);
                }
            }

            if (el.Location is LocationPoint lp2) return lp2.Point;
            if (el.Location is LocationCurve lc)
                return (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2;

            var geoOpt = new Options { ComputeReferences = false };
            var geoElem = el.get_Geometry(geoOpt);
            if (geoElem != null)
            {
                double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
                bool hasGeo = false;

                foreach (GeometryObject geoObj in geoElem)
                {
                    Solid solid = geoObj as Solid;
                    if (solid == null || solid.Volume <= 0) continue;
                    foreach (Edge edge in solid.Edges)
                    {
                        XYZ pt = edge.AsCurve().GetEndPoint(0);
                        minX = Math.Min(minX, pt.X); maxX = Math.Max(maxX, pt.X);
                        minY = Math.Min(minY, pt.Y); maxY = Math.Max(maxY, pt.Y);
                        minZ = Math.Min(minZ, pt.Z); maxZ = Math.Max(maxZ, pt.Z);
                        hasGeo = true;
                    }
                }

                if (hasGeo)
                    return new XYZ((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2);
            }

            return null;
        }

        private ElementId _solidFillId;

        private ElementId GetSolidFillId()
        {
            if (_solidFillId != null) return _solidFillId;
            foreach (FillPatternElement fp in new FilteredElementCollector(_doc).OfClass(typeof(FillPatternElement)))
            {
                if (fp.GetFillPattern().IsSolidFill)
                    return _solidFillId = fp.Id;
            }
            return _solidFillId = ElementId.InvalidElementId;
        }
    }
}