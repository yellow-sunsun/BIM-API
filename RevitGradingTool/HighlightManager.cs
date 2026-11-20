using Autodesk.Revit.DB;
using System;
using System.Collections.Generic;

namespace RevitGradingTool
{
    public class HighlightManager
    {
        private Document _doc;
        private List<ElementId> _highlightedIds = new List<ElementId>();
        private List<ElementId> _textNoteIds = new List<ElementId>();

        public HighlightManager(Document doc)
        {
            _doc = doc;
        }

        public void ClearAll()
        {
            using (var tx = new Transaction(_doc, "清除標色"))
            {
                tx.Start();

                foreach (var id in _highlightedIds)
                {
                    var el = _doc.GetElement(id);
                    if (el != null)
                        _doc.ActiveView.SetElementOverrides(id, new OverrideGraphicSettings());
                }

                foreach (var id in _textNoteIds)
                {
                    var el = _doc.GetElement(id);
                    if (el != null)
                        _doc.Delete(id);
                }

                tx.Commit();
            }

            _highlightedIds.Clear();
            _textNoteIds.Clear();
        }

        // 標準模型標色（用標準座標）
        public void Highlight(List<CompareError> errors, ModelSnapshot standard)
        {
            ClearAll();

            using (var tx = new Transaction(_doc, "標色學生錯誤"))
            {
                tx.Start();

                var textNoteTypeId = _doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

                foreach (var err in errors)
                {
                    XYZ position = new XYZ(err.X / 304.8, err.Y / 304.8, err.Z / 304.8);

                    if (err.Type == ErrorType.Extra)
                    {
                        var textNoteOptions = new TextNoteOptions
                        {
                            HorizontalAlignment = HorizontalTextAlignment.Center,
                            TypeId = textNoteTypeId
                        };
                        TextNote note = TextNote.Create(
                            _doc, _doc.ActiveView.Id, position,
                            "\u2b06 \u591a\u9918\u69cb\u4ef6", textNoteOptions);
                        _textNoteIds.Add(note.Id);
                    }
                    else
                    {
                        ApplyColor(err, position);
                    }
                }

                tx.Commit();
            }
        }

        // 學生模型標色（用學生座標）
        public void HighlightStudent(List<CompareError> errors)
        {
            ClearAll();

            using (var tx = new Transaction(_doc, "\u6a19\u8272\u5b78\u751f\u932f\u8aa4"))
            {
                tx.Start();

                var textNoteTypeId = _doc.GetDefaultElementTypeId(ElementTypeGroup.TextNoteType);

                foreach (var err in errors)
                {
                    XYZ position = new XYZ(err.StudentX / 304.8, err.StudentY / 304.8, err.StudentZ / 304.8);

                    if (err.Type == ErrorType.Extra)
                    {
                        var textNoteOptions = new TextNoteOptions
                        {
                            HorizontalAlignment = HorizontalTextAlignment.Center,
                            TypeId = textNoteTypeId
                        };
                        TextNote note = TextNote.Create(
                            _doc, _doc.ActiveView.Id, position,
                            "\u2b06 \u591a\u9918\u69cb\u4ef6", textNoteOptions);
                        _textNoteIds.Add(note.Id);
                    }
                    else if (err.Type == ErrorType.Missing)
                    {
                        // 在學生模型對應位置放箭頭提示
                        var textNoteOptions = new TextNoteOptions
                        {
                            HorizontalAlignment = HorizontalTextAlignment.Center,
                            TypeId = textNoteTypeId
                        };
                        TextNote note = TextNote.Create(
                            _doc, _doc.ActiveView.Id, position,
                            "\u2b07 \u6b64\u8655\u7f3a\u5c11\u69cb\u4ef6", textNoteOptions);
                        _textNoteIds.Add(note.Id);
                        continue;
                    }
                    else
                    {
                        ApplyColor(err, position);
                    }
                }

                tx.Commit();
            }
        }

        private void ApplyColor(CompareError err, XYZ position)
        {
            double tol = (err.Category == "\u6a13\u677f" || err.Category == "\u7246")
                ? 500.0 / 304.8 : 50.0 / 304.8;

            var collector = new FilteredElementCollector(_doc).WhereElementIsNotElementType();
            Element bestEl = null;
            double bestDist = double.MaxValue;

            foreach (Element el in collector)
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

            if (bestEl != null)
            {
                var ovr = new OverrideGraphicSettings();

                if (err.Type == ErrorType.Missing)
                {
                    ovr.SetSurfaceForegroundPatternColor(new Color(255, 0, 0));
                    ovr.SetSurfaceForegroundPatternId(GetSolidFillId());
                }
                else if (err.Type == ErrorType.WrongRotation || err.Type == ErrorType.WrongPosition)
                {
                    ovr.SetSurfaceForegroundPatternColor(new Color(255, 255, 0));
                    ovr.SetSurfaceForegroundPatternId(GetSolidFillId());
                }
                else
                {
                    ovr.SetSurfaceForegroundPatternColor(new Color(255, 140, 0));
                    ovr.SetSurfaceForegroundPatternId(GetSolidFillId());
                }

                _doc.ActiveView.SetElementOverrides(bestEl.Id, ovr);
                _highlightedIds.Add(bestEl.Id);
            }
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

        private ElementId GetSolidFillId()
        {
            var patterns = new FilteredElementCollector(_doc)
                .OfClass(typeof(FillPatternElement));
            foreach (FillPatternElement fp in patterns)
            {
                if (fp.GetFillPattern().IsSolidFill)
                    return fp.Id;
            }
            return ElementId.InvalidElementId;
        }
    }
}