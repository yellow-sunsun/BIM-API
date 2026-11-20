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

            if (_standardManager == null)
                _standardManager = new HighlightManager(standardDoc);

            _standardManager.ClearAll();

            // 關閉上一個學生模型
            if (_studentDoc != null)
            {
                try
                {
                    if (_studentDoc.PathName != standardDoc.PathName)
                        _studentDoc.Close(false);
                }
                catch { }
                _studentDoc = null;
                _studentManager = null;
            }

            if (Errors == null || Errors.Count == 0)
                return;

            _standardManager.Highlight(Errors, Standard);

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
                    _studentManager.HighlightStudent(Errors);

                    // 切換回標準模型讓它在左邊
                    app.OpenAndActivateDocument(standardDoc.PathName);

                    // 並排視窗
                    var tileCmd = RevitCommandId.LookupPostableCommandId(PostableCommand.TileViews);
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