using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System.Collections.Generic;
using System.Linq;

namespace RevitGradingTool
{
    public enum HighlightMode
    {
        Highlight,   // 標色：開啟學生模型、兩邊都標色並排
        Focus        // 放大到某一個錯誤
    }

    public class HighlightEvent : IExternalEventHandler
    {
        public HighlightMode Mode { get; set; } = HighlightMode.Highlight;
        public int FocusIndex { get; set; }
        public List<CompareError> Errors { get; set; }
        public ModelSnapshot Standard { get; set; }
        public string StudentRvtPath { get; set; }
        public UIApplication UIApp { get; set; }

        private HighlightManager _standardManager;
        private Document _standardDoc;
        private HighlightManager _studentManager;
        private Document _studentDoc;
        private List<CompareError> _shownErrors;   // 目前畫面上標色的是哪一份錯誤清單

        public void Execute(UIApplication app)
        {
            try
            {
                if (Mode == HighlightMode.Focus)
                    DoFocus(app);
                else
                    DoHighlight(app);
            }
            catch (System.Exception ex)
            {
                TaskDialog.Show("\u932f\u8aa4", ex.Message);
            }
        }

        private void DoHighlight(UIApplication app)
        {
            // 使用者可能點了學生模型的視窗，這時作用中文件是學生模型，不是標準模型
            var active = app.ActiveUIDocument.Document;
            var standardDoc = (_standardDoc != null && _standardDoc.IsValidObject &&
                               _studentDoc != null && _studentDoc.IsValidObject && active.Equals(_studentDoc))
                ? _standardDoc : active;

            if (_standardManager != null && _standardDoc != null && _standardDoc.IsValidObject)
                _standardManager.ClearAll();

            // 關閉上一個學生模型
            if (_studentDoc != null)
            {
                try
                {
                    if (_studentDoc.IsValidObject && _studentDoc.PathName != standardDoc.PathName)
                    {
                        if (app.ActiveUIDocument.Document.Equals(_studentDoc))
                            app.OpenAndActivateDocument(standardDoc.PathName);
                        _studentDoc.Close(false);
                    }
                }
                catch { }
                _studentDoc = null;
                _studentManager = null;
            }
            _shownErrors = null;

            if (Errors == null || Errors.Count == 0)
                return;

            // 標準模型切到 3D 視圖
            var stdUi = app.ActiveUIDocument;
            var stdView = Show3D(stdUi);
            _standardDoc = standardDoc;
            _standardManager = new HighlightManager(standardDoc, stdView);
            stdUi.GetOpenUIViews().FirstOrDefault(v => v.ViewId == stdView.Id)?.ZoomToFit();
            _standardManager.Highlight(Errors, Standard);
            _shownErrors = Errors;

            if (string.IsNullOrEmpty(StudentRvtPath))
                return;

            try
            {
                var uiDoc = app.OpenAndActivateDocument(StudentRvtPath);
                _studentDoc = uiDoc?.Document;

                if (_studentDoc == null)
                {
                    TaskDialog.Show("\u932f\u8aa4", $"\u7121\u6cd5\u958b\u555f\uff1a{StudentRvtPath}");
                    return;
                }

                // 學生模型也切到 3D 視圖
                var stuView = Show3D(uiDoc);
                uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == stuView.Id)?.ZoomToFit();
                _studentManager = new HighlightManager(_studentDoc, stuView);
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
                TaskDialog.Show("\u958b\u555f\u5b78\u751f\u6a21\u578b\u5931\u6557", $"\u8def\u5f91\uff1a{StudentRvtPath}\n\u932f\u8aa4\uff1a{ex.Message}");
            }
        }

        private void DoFocus(UIApplication app)
        {
            if (Errors == null || FocusIndex < 0 || FocusIndex >= Errors.Count)
                return;

            // 還沒標色、或換了學生 / 樓層：先標色並開啟學生模型
            bool ready = _shownErrors == Errors && _standardManager != null &&
                         _standardDoc != null && _standardDoc.IsValidObject &&
                         (string.IsNullOrEmpty(StudentRvtPath) ||
                          (_studentDoc != null && _studentDoc.IsValidObject));
            if (!ready)
            {
                Mode = HighlightMode.Highlight;
                DoHighlight(app);
            }
            if (_standardManager == null) return;

            var err = Errors[FocusIndex];
            bool stdActive = app.ActiveUIDocument.Document.Equals(_standardDoc);

            // 建築中心（mm）：決定鏡頭從哪一側看進去。學生模型可能整棟偏移，
            // 用這個錯誤的「標準座標 - 學生原始座標」換算學生模型的建築中心
            XYZ stdCenter = BuildingCenter();
            XYZ shift = new XYZ(err.X - err.RawStudentX, err.Y - err.RawStudentY, err.Z - err.RawStudentZ);

            // 標準模型：錯誤在標準座標的位置；學生模型：學生原始座標的位置
            _standardManager.FocusOn(err, new XYZ(err.X, err.Y, err.Z), stdCenter, stdActive);
            if (_studentManager != null && _studentDoc != null && _studentDoc.IsValidObject)
                _studentManager.FocusOn(err, new XYZ(err.RawStudentX, err.RawStudentY, err.RawStudentZ),
                    stdCenter - shift, !stdActive);
        }

        private ModelSnapshot _centerOf;
        private XYZ _center = XYZ.Zero;

        // 標準模型所有構件的平均位置（mm）
        private XYZ BuildingCenter()
        {
            if (Standard == null || Standard.Elements.Count == 0) return XYZ.Zero;
            if (_centerOf == Standard) return _center;
            double x = 0, y = 0, z = 0;
            foreach (var el in Standard.Elements) { x += el.X; y += el.Y; z += el.Z; }
            int n = Standard.Elements.Count;
            _centerOf = Standard;
            _center = new XYZ(x / n, y / n, z / n);
            return _center;
        }

        // 把文件切到 3D 視圖（找預設 {3D}，沒有就建立），回傳該視圖
        private static View Show3D(UIDocument uiDoc)
        {
            var v3 = HighlightManager.Get3DView(uiDoc.Document);
            if (v3 == null) return uiDoc.ActiveView;
            if (uiDoc.ActiveView == null || uiDoc.ActiveView.Id != v3.Id)
                uiDoc.ActiveView = v3;

            // 關掉這個模型其他開著的視圖（例如原本開著的 FL1 平面圖），
            // 並排時就只會有「標準模型 3D」和「學生模型 3D」兩個視窗
            foreach (var uv in uiDoc.GetOpenUIViews())
            {
                if (uv.ViewId == v3.Id) continue;
                try { uv.Close(); } catch { }
            }
            return v3;
        }

        public string GetName() => "HighlightEvent";
    }
}