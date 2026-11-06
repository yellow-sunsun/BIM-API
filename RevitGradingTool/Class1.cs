using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Text;

namespace RevitGradingTool
{
    [Transaction(TransactionMode.Manual)]
    public class HelloRevit : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // 收集所有柱
            FilteredElementCollector collector = new FilteredElementCollector(doc);
            collector.OfCategory(BuiltInCategory.OST_StructuralColumns)
                     .WhereElementIsNotElementType();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"找到 {collector.GetElementCount()} 根柱：\n");

            foreach (Element col in collector)
            {
                LocationPoint loc = col.Location as LocationPoint;
                if (loc != null)
                {
                    XYZ pt = loc.Point;
                    // Revit 內部單位是英尺，乘以 304.8 換算成 mm
                    double x = pt.X * 304.8;
                    double y = pt.Y * 304.8;
                    double z = pt.Z * 304.8;
                    sb.AppendLine($"柱 {col.Id}: X={x:F0}mm, Y={y:F0}mm, Z={z:F0}mm");
                }
            }

            TaskDialog.Show("柱座標清單", sb.ToString());
            return Result.Succeeded;
        }
    }
}