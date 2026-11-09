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
        public int Id { get; set; }
        public string Category { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string FamilyType { get; set; }
        public string Material { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Depth { get; set; }
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
                        Id = el.Id.IntegerValue,
                        Category = el.Category?.Name ?? "Unknown"
                    };

                    if (el.Location is LocationPoint lp)
                    {
                        snap.X = lp.Point.X * 304.8;
                        snap.Y = lp.Point.Y * 304.8;
                        snap.Z = lp.Point.Z * 304.8;
                    }
                    else if (el.Location is LocationCurve lc)
                    {
                        XYZ mid = (lc.Curve.GetEndPoint(0) + lc.Curve.GetEndPoint(1)) / 2;
                        snap.X = mid.X * 304.8;
                        snap.Y = mid.Y * 304.8;
                        snap.Z = mid.Z * 304.8;
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