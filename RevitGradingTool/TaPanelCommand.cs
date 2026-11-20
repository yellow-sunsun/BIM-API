using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
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
            UIApplication uiApp = commandData.Application;
            Document doc = uiApp.ActiveUIDocument.Document;

            var highlightEvent = new HighlightEvent();
            var exEvent = ExternalEvent.Create(highlightEvent);

            var panel = new TaPanel();

            // 標色功能
            panel.HighlightRequested += (s, student) =>
            {
                highlightEvent.Errors = student.Errors;
                highlightEvent.Standard = panel.StandardSnapshot;
                highlightEvent.StudentRvtPath = student.RvtPath;
                highlightEvent.UIApp = uiApp;
                exEvent.Raise();
            };

            // 清除標色
            panel.ClearRequested += (s, e) =>
            {
                highlightEvent.Errors = new List<CompareError>();
                highlightEvent.Standard = panel.StandardSnapshot;
                highlightEvent.StudentRvtPath = null;
                highlightEvent.UIApp = uiApp;
                exEvent.Raise();
            };

            // Excel 匯出
            panel.ExportRequested += (s, e) =>
            {
                var dlg = new SaveFileDialog
                {
                    Title = "\u532f\u51fa Excel \u5831\u544a",
                    Filter = "CSV files|*.csv",
                    FileName = "BIM\u4f5c\u696d\u6279\u6539\u5831\u544a.csv"
                };
                if (dlg.ShowDialog() != DialogResult.OK) return;

                var exporter = new ExcelExporter();
                exporter.Export(panel.Students, dlg.FileName);
                TaskDialog.Show("\u532f\u51fa\u5b8c\u6210", $"Excel \u5df2\u5132\u5b58\u81f3\uff1a\n{dlg.FileName}");
            };

            panel.Show();
            return Result.Succeeded;
        }
    }
}