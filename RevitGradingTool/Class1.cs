using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace RevitGradingTool
{
    // 單一構件的資料結構
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

    // 整份模型快照
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

            ModelSnapshot snapshot = new ModelSnapshot
            {
                ModelName = doc.Title
            };

            // 要蒐集的構件類別
            var categories = new[]
            {
                BuiltInCategory.OST_StructuralColumns, // 柱
                BuiltInCategory.OST_StructuralFraming,  // 梁
                BuiltInCategory.OST_Floors,             // 樓板
                BuiltInCategory.OST_Walls,              // 牆
                BuiltInCategory.OST_Doors,              // 門
                BuiltInCategory.OST_Windows             // 窗
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

                    // 取得座標（點構件用 LocationPoint，線構件用 LocationCurve 中點）
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

                    // 族群類型名稱
                    if (el is FamilyInstance fi)
                    {
                        snap.FamilyType = fi.Symbol?.Name ?? "";
                    }
                    else
                    {
                        snap.FamilyType = el.Name;
                    }

                    // 材質（取第一個材質）
                    var matIds = el.GetMaterialIds(false);
                    if (matIds.Count > 0)
                    {
                        Material mat = doc.GetElement(matIds.First()) as Material;
                        snap.Material = mat?.Name ?? "";
                    }

                    // 尺寸參數
                    snap.Width = GetParamMM(el, BuiltInParameter.GENERIC_WIDTH);
                    snap.Height = GetParamMM(el, BuiltInParameter.GENERIC_HEIGHT);
                    snap.Depth = GetParamMM(el, BuiltInParameter.GENERIC_DEPTH);

                    snapshot.Elements.Add(snap);
                }
            }

            // 存成 JSON
            string json = JsonConvert.SerializeObject(snapshot, Newtonsoft.Json.Formatting.Indented);
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                doc.Title + "_snapshot.json");
            File.WriteAllText(path, json);

            TaskDialog.Show("匯出完成",
                $"共匯出 {snapshot.Elements.Count} 個構件\n存檔位置：{path}");

            return Result.Succeeded;
        }

        private double GetParamMM(Element el, BuiltInParameter bip)
        {
            Parameter p = el.get_Parameter(bip);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                return p.AsDouble() * 304.8;
            return 0;
        }
    }
}