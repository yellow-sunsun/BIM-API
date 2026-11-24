using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitGradingTool
{
    public class ElementSnapshot
    {
        public long Id { get; set; }
        public string Category { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string FamilyType { get; set; }
        public string Material { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Depth { get; set; }
        public double Rotation { get; set; }
        public string Level { get; set; }  // 新增：構件所在樓層
    }

    public class ModelSnapshot
    {
        public string ModelName { get; set; }
        public List<ElementSnapshot> Elements { get; set; } = new List<ElementSnapshot>();
    }

    [Transaction(TransactionMode.Manual)]
    public class ExportSnapshot : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;
            var snapshot = ParseDocument(doc);

            string json = JsonConvert.SerializeObject(snapshot, Newtonsoft.Json.Formatting.Indented);
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                doc.Title + "_snapshot.json");
            File.WriteAllText(path, json);

            TaskDialog.Show("\u532f\u51fa\u5b8c\u6210",
                $"\u5171\u532f\u51fa {snapshot.Elements.Count} \u500b\u69cb\u4ef6\n\u5b58\u6a94\u4f4d\u7f6e\uff1a{path}");

            return Result.Succeeded;
        }

        public static ModelSnapshot ParseDocument(Document doc)
        {
            var snapshot = new ModelSnapshot { ModelName = doc.Title };

            // 所有樓層依高度排序，找不到樓層參數時用高度判斷
            var levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).ToList();


            var categories = new[]
            {
                BuiltInCategory.OST_StructuralColumns,
                BuiltInCategory.OST_StructuralFraming,
                BuiltInCategory.OST_Floors,
                BuiltInCategory.OST_Walls,
                BuiltInCategory.OST_Doors,
                BuiltInCategory.OST_Windows
            };

            foreach (var cat in categories)
            {
                var collector = new FilteredElementCollector(doc)
                    .OfCategory(cat)
                    .WhereElementIsNotElementType();

                foreach (Element el in collector)
                {
                    var snap = new ElementSnapshot
                    {
                        Id = el.Id.Value,
                        Category = el.Category?.Name ?? "Unknown"
                    };

                    // 內建族群（in-place）的定位點固定在專案原點 (0,0,0)，跟構件實際位置無關，
                    // 整棟偏移時定位點也不會跟著動。改用外框中心當位置
                    BoundingBoxXYZ inPlaceBox = (el is FamilyInstance fiIp && fiIp.Symbol?.Family?.IsInPlace == true)
                        ? el.get_BoundingBox(null) : null;

                    if (inPlaceBox != null)
                    {
                        snap.X = ((inPlaceBox.Min.X + inPlaceBox.Max.X) / 2) * 304.8;
                        snap.Y = ((inPlaceBox.Min.Y + inPlaceBox.Max.Y) / 2) * 304.8;
                        snap.Z = ((inPlaceBox.Min.Z + inPlaceBox.Max.Z) / 2) * 304.8;
                        snap.Rotation = 0;
                    }
                    else if (el.Location is LocationPoint lp)
                    {

                        if (el.Category?.Name == "\u7d50\u69cb\u67f1")
                        {
                            BoundingBoxXYZ bb = el.get_BoundingBox(null);
                            if (bb != null)
                            {
                                snap.X = lp.Point.X * 304.8;
                                snap.Y = lp.Point.Y * 304.8;
                                snap.Z = ((bb.Min.Z + bb.Max.Z) / 2) * 304.8;
                            }
                            else
                            {
                                snap.X = lp.Point.X * 304.8;
                                snap.Y = lp.Point.Y * 304.8;
                                snap.Z = lp.Point.Z * 304.8;
                            }
                        }
                        else
                        {
                            snap.X = lp.Point.X * 304.8;
                            snap.Y = lp.Point.Y * 304.8;
                            snap.Z = lp.Point.Z * 304.8;
                        }

                        if (el is FamilyInstance fiLp)
                        {
                            var transform = fiLp.GetTotalTransform();
                            snap.Rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * (180.0 / Math.PI);
                        }
                        else
                        {
                            snap.Rotation = lp.Rotation * (180.0 / Math.PI);
                        }
                    }
                    else if (el.Location is LocationCurve lc)
                    {
                        XYZ mid = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2;
                        snap.X = mid.X * 304.8;
                        snap.Y = mid.Y * 304.8;
                        snap.Z = mid.Z * 304.8;

                        if (el is FamilyInstance fiLc)
                        {
                            var transform = fiLc.GetTotalTransform();
                            snap.Rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * (180.0 / Math.PI);
                        }
                        else
                        {
                            XYZ dir = (lc.Curve.GetEndPoint(1) - lc.Curve.GetEndPoint(0)).Normalize();
                            snap.Rotation = Math.Atan2(dir.Y, dir.X) * (180.0 / Math.PI);
                        }
                    }
                    else
                    {
                        bool positioned = false;
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
                            {
                                snap.X = ((minX + maxX) / 2) * 304.8;
                                snap.Y = ((minY + maxY) / 2) * 304.8;
                                snap.Z = ((minZ + maxZ) / 2) * 304.8;
                                positioned = true;
                            }
                        }

                        // 族群（例如內建族群的梁）的幾何包在 GeometryInstance 裡，上面抓不到實體，
                        // 原本會留在 (0,0,0)：所有這類構件擠在原點、整棟偏移時全部被判錯。改用外框中心
                        if (!positioned)
                        {
                            BoundingBoxXYZ bbox = el.get_BoundingBox(null);
                            if (bbox != null)
                            {
                                snap.X = ((bbox.Min.X + bbox.Max.X) / 2) * 304.8;
                                snap.Y = ((bbox.Min.Y + bbox.Max.Y) / 2) * 304.8;
                                snap.Z = ((bbox.Min.Z + bbox.Max.Z) / 2) * 304.8;
                            }
                        }

                        if (el is FamilyInstance fiElse)
                        {
                            var transform = fiElse.GetTotalTransform();
                            snap.Rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * (180.0 / Math.PI);
                        }
                    }

                    // 讀取構件所在樓層（不同類別用不同參數）
                    Parameter levelParam = null;

                    if (el.Category?.Name == "\u7d50\u69cb\u69cb\u67b6")
                    {
                        levelParam = el.get_Parameter(BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM);
                    }
                    else if (el.Category?.Name == "\u6a13\u677f")
                    {
                        levelParam = el.get_Parameter(BuiltInParameter.LEVEL_PARAM);
                    }
                    else
                    {
                        levelParam = el.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM)
                                  ?? el.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
                    }

                    if (levelParam != null && levelParam.HasValue &&
                        levelParam.StorageType == StorageType.ElementId)
                    {
                        var levelEl = doc.GetElement(levelParam.AsElementId());
                        snap.Level = levelEl?.Name ?? "";
                    }

                    // 參數讀不到樓層（例如內建族群）：先看元素本身的 LevelId，再依高度找所在樓層
                    if (string.IsNullOrEmpty(snap.Level))
                    {
                        Level lv = el.LevelId != ElementId.InvalidElementId ? doc.GetElement(el.LevelId) as Level : null;
                        if (lv == null) lv = LevelAtHeight(levels, snap.Z / 304.8);
                        snap.Level = lv?.Name ?? "";
                    }

                    if (el is FamilyInstance fi)
                        snap.FamilyType = fi.Symbol?.Name ?? "";
                    else
                        snap.FamilyType = el.Name;

                    var matIds = el.GetMaterialIds(false);
                    if (matIds.Count > 0)
                    {
                        Material mat = doc.GetElement(matIds.First()) as Material;
                        snap.Material = mat?.Name ?? "";
                    }

                    snap.Width = GetParamMM(el, BuiltInParameter.GENERIC_WIDTH);
                    snap.Height = GetParamMM(el, BuiltInParameter.GENERIC_HEIGHT);
                    snap.Depth = GetParamMM(el, BuiltInParameter.GENERIC_DEPTH);

                    snapshot.Elements.Add(snap);
                }
            }

            return snapshot;
        }
        // 依高度找構件所在樓層：取「高度 <= 構件中心 + 0.5m」的最高樓層。
        // 多 0.5m 是因為梁、樓板的中心略低於它所屬的樓層線
        private static Level LevelAtHeight(List<Level> levels, double zFt)
        {
            if (levels.Count == 0) return null;
            Level found = levels[0];
            foreach (var lv in levels)
            {
                if (lv.ProjectElevation <= zFt + 500 / 304.8) found = lv;
                else break;
            }
            return found;
        }

        private static double GetParamMM(Element el, BuiltInParameter bip)

        {
            Parameter p = el.get_Parameter(bip);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                return p.AsDouble() * 304.8;
            return 0;
        }
    }
}