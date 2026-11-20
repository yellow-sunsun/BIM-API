using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Windows.Forms;

namespace RevitGradingTool
{
    [Transaction(TransactionMode.Manual)]
    public class TaPanelCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument.Document;

            // 建立 ExternalEvent
            var highlightEvent = new HighlightEvent();
            var exEvent = ExternalEvent.Create(highlightEvent);

            var panel = new TaPanel();

            // 標色功能：透過 ExternalEvent 呼叫
            panel.HighlightRequested += (s, student) =>
            {
                highlightEvent.Errors = student.Errors;
                highlightEvent.Standard = panel.StandardSnapshot;
                exEvent.Raise();
            };

            // 清除標色
            panel.ClearRequested += (s, e) =>
            {
                highlightEvent.Errors = new System.Collections.Generic.List<CompareError>();
                highlightEvent.Standard = panel.StandardSnapshot;
                exEvent.Raise();
            };

            // Excel 匯出
            panel.ExportRequested += (s, e) =>
            {
                var dlg = new SaveFileDialog
                {
                    Title = "匯出 Excel 報告",
                    Filter = "CSV files|*.csv",
                    FileName = "BIM作業批改報告.csv"
                };
                if (dlg.ShowDialog() != DialogResult.OK) return;

                var exporter = new ExcelExporter();
                exporter.Export(panel.Students, dlg.FileName);
                TaskDialog.Show("匯出完成", $"Excel 已儲存至：\n{dlg.FileName}");
            };

            panel.Show();
            return Result.Succeeded;
        }
    }
}