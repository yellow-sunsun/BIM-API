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
        public string Level { get; set; }  // 穝糤篶ン┮加糷
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

                    if (el.Location is LocationPoint lp)
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
                            }
                        }

                        if (el is FamilyInstance fiElse)
                        {
                            var transform = fiElse.GetTotalTransform();
                            snap.Rotation = Math.Atan2(transform.BasisX.Y, transform.BasisX.X) * (180.0 / Math.PI);
                        }
                    }

                    // 弄篶ン┮加糷ぃ摸ノぃ把计
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
        private static double GetParamMM(Element el, BuiltInParameter bip)
        {
            Parameter p = el.get_Parameter(bip);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                return p.AsDouble() * 304.8;
            return 0;
        }
    }
}