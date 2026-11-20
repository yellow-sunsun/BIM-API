using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;

namespace RevitGradingTool
{
    public class HighlightEvent : IExternalEventHandler
    {
        public List<CompareError> Errors { get; set; }
        public ModelSnapshot Standard { get; set; }
        public string StudentRvtPath { get; set; }
        public UIApplication UIApp { get; set; }

        private HighlightManager _standardManager;
        private HighlightManager _studentManager;
        private Document _studentDoc;

        public void Execute(UIApplication app)
        {
            var standardDoc = app.ActiveUIDocument.Document;

            // 建立或重用標準模型的 HighlightManager
            if (_standardManager == null)
                _standardManager = new HighlightManager(standardDoc);

            // 先清除標準模型的標色
            _standardManager.ClearAll();

            // 先清除並關閉上一個學生模型
            if (_studentDoc != null)
            {
                try
                {
                    _studentManager?.ClearAll();
                    if (_studentDoc.PathName != standardDoc.PathName)
                        _studentDoc.Close(false);
                }
                catch { }
                _studentDoc = null;
                _studentManager = null;
            }

            // 如果沒有錯誤（清除模式）就到此結束
            if (Errors == null || Errors.Count == 0)
                return;

            // 標準模型標色
            _standardManager.Highlight(Errors, Standard);

            // 開啟學生模型並標色
            if (!string.IsNullOrEmpty(StudentRvtPath))
            {
                try
                {
                    var uiDoc = app.OpenAndActivateDocument(StudentRvtPath);
                    _studentDoc = uiDoc?.Document;

                    if (_studentDoc == null)
                    {
                        TaskDialog.Show("\u932f\u8aa4", $"\u7121\u6cd5\u958b\u555f\uff1a{StudentRvtPath}");
                        return;
                    }

                    _studentManager = new HighlightManager(_studentDoc);
                    _studentManager.Highlight(Errors, Standard);

                    // 並排視窗
                    var tileCmd = RevitCommandId.LookupCommandId("View.TileWindows");
                    if (tileCmd != null)
                        app.PostCommand(tileCmd);
                }
                catch (System.Exception ex)
                {
                    TaskDialog.Show("\u958b\u555f\u5b78\u751f\u6a21\u578b\u5931\u6557",
                        $"\u8def\u5f91\uff1a{StudentRvtPath}\n\u932f\u8aa4\uff1a{ex.Message}");
                }
            }
        }

        public string GetName() => "HighlightEvent";
    }
}