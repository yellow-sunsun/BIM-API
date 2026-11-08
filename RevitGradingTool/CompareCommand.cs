using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RevitGradingTool
{
    [Transaction(TransactionMode.Manual)]
    public class CompareCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            // 選標準模型 JSON
            var dlg1 = new OpenFileDialog
            {
                Title = "選擇標準模型 JSON",
                Filter = "JSON files|*.json"
            };
            if (dlg1.ShowDialog() != DialogResult.OK) return Result.Cancelled;

            // 選學生模型 JSON
            var dlg2 = new OpenFileDialog
            {
                Title = "選擇學生模型 JSON",
                Filter = "JSON files|*.json"
            };
            if (dlg2.ShowDialog() != DialogResult.OK) return Result.Cancelled;

            // 讀取兩份快照
            var standard = JsonConvert.DeserializeObject<ModelSnapshot>(
                File.ReadAllText(dlg1.FileName));
            var student = JsonConvert.DeserializeObject<ModelSnapshot>(
                File.ReadAllText(dlg2.FileName));

            // 執行比對
            var engine = new ComparisonEngine();
            var errors = engine.Compare(standard, student);

            // 顯示結果
            if (errors.Count == 0)
            {
                TaskDialog.Show("比對結果", "完全正確！這位學生是標準模型的親生孩子！");
                return Result.Succeeded;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"共發現 {errors.Count} 個錯誤：\n");

            foreach (var err in errors)
            {
                string icon = err.Type == ErrorType.Missing ? "缺少    " :
                              err.Type == ErrorType.Extra ? "多餘    " : "屬性錯誤";
                sb.AppendLine($"[{icon}] {err.Category}");
                sb.AppendLine($"  {err.Detail}");
                sb.AppendLine($"  位置：X={err.X:F0}, Y={err.Y:F0}, Z={err.Z:F0}\n");
            }

            TaskDialog.Show("比對結果", sb.ToString());
            return Result.Succeeded;
        }
    }
}