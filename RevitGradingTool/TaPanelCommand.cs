using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

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
            var panel = new TaPanel();

            // 標色功能留到第五週接上，這週先顯示面板
            panel.HighlightRequested += (s, student) =>
            {
                TaskDialog.Show("標色",
                    $"即將標色【{student.Name}】的 {student.Errors.Count} 個錯誤\n（標色功能下週完成）");
            };

            panel.Show();
            return Result.Succeeded;
        }
    }
}