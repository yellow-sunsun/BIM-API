using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using System;
using System.Collections.Generic;
using System.IO;
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
            ResetLog();
            LogWindows(app, "\u958b\u59cb\u6a19\u8272\u524d");

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
            _stdView = stdView;
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
                _stuView = stuView;
                uiDoc.GetOpenUIViews().FirstOrDefault(v => v.ViewId == stuView.Id)?.ZoomToFit();
                _studentManager = new HighlightManager(_studentDoc, stuView);
                _studentManager.HighlightStudent(Errors);

                // 切換回標準模型讓它在左邊。Revit 切回時有時會再開一個標準模型的視窗，
                // 只留下標準模型的 3D 視窗，並排後才會剛好是「標準 3D＋學生 3D」兩個
                var stdUiBack = app.OpenAndActivateDocument(standardDoc.PathName);
                LogWindows(app, "\u5207\u56de\u6a19\u6e96\u6a21\u578b\u5f8c\uff08API \u57f7\u884c\u4e2d\uff09");
                KeepSingleWindow(stdUiBack, stdView);
                KeepSingleWindow(new UIDocument(_studentDoc), stuView);

                // 並排視窗
                var tileCmd = RevitCommandId.LookupPostableCommandId(PostableCommand.TileViews);
                if (tileCmd != null)
                    app.PostCommand(tileCmd);

                // Revit 會在這一步結束後才建立（切回標準模型時多出來的）視窗，
                // 所以等 Revit 閒置（所有指令、畫面更新都完成）時再收尾：關掉多的視窗、重新並排
                StartCleanup(app);
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
            if (!ready && !_focusDeferred)
            {
                DoHighlight(app);
                // 剛開啟學生模型時，「並排視窗」要等這次執行結束才會生效，這時放大學生視圖會是空白的。
                // 先結束這次，等閒置收尾（關多的視窗、並排）完成後，再自動執行一次放大
                if (Self != null && _cleanupActive)
                {
                    _focusDeferred = true;
                    Mode = HighlightMode.Focus;
                    return;
                }
            }
            _focusDeferred = false;
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

        // 自己的 ExternalEvent（由 TaPanelCommand 設定），用來在並排視窗後再執行一次放大
        public ExternalEvent Self { get; set; }
        private bool _focusDeferred;
        private View _stdView;
        private View _stuView;

        // ---- 閒置收尾：Revit 處理完所有指令、畫面也更新後（Idling 事件），關掉多的視窗、重新並排 ----
        private bool _cleanupActive;
        private int _idleTicks;
        private int _cleanRounds;
        private bool _tiledAfterClean;

        private void StartCleanup(UIApplication app)
        {
            _idleTicks = 0;
            _cleanRounds = 0;
            _tiledAfterClean = false;
            if (!_cleanupActive)
            {
                app.Idling += OnIdling;
                _cleanupActive = true;
            }
            Log("\u5df2\u6392\u5b9a\u9592\u7f6e\u6536\u5c3e");
        }

        private void StopCleanup(UIApplication app, string reason)
        {
            if (_cleanupActive)
            {
                app.Idling -= OnIdling;
                _cleanupActive = false;
            }
            Log("\u9592\u7f6e\u6536\u5c3e\u7d50\u675f\uff1a" + reason);
            LogWindows(app, "\u6700\u5f8c\u7684\u8996\u7a97");
            // 「顯示目前錯誤」要等視窗整理好才放大
            if (_focusDeferred && Self != null) Self.Raise();
        }

        private void OnIdling(object sender, IdlingEventArgs e)
        {
            var app = sender as UIApplication;
            if (app == null) return;
            try
            {
                _idleTicks++;
                if (_idleTicks < 2) return;   // 多等一次閒置，讓「並排視窗」先執行完

                int extra = ExtraWindowCount();
                LogWindows(app, $"\u9592\u7f6e\u7b2c {_idleTicks} \u6b21\uff08\u591a\u51fa {extra} \u500b\u8996\u7a97\uff09");
                if (extra > 0)
                {
                    if (_cleanRounds >= 5) { StopCleanup(app, "\u95dc\u4e86 5 \u6b21\u9084\u662f\u6709\u591a\u7684\u8996\u7a97\uff0c\u653e\u68c4"); return; }
                    _cleanRounds++;
                    CloseExtraWindows();
                    _tiledAfterClean = false;
                    return;   // 下一次閒置再檢查一次
                }
                if (!_tiledAfterClean && _cleanRounds > 0)
                {
                    // 關過視窗：重新並排一次，下一次閒置再確認
                    var tileCmd = RevitCommandId.LookupPostableCommandId(PostableCommand.TileViews);
                    if (tileCmd != null) app.PostCommand(tileCmd);
                    _tiledAfterClean = true;
                    Log("\u95dc\u6389\u591a\u7684\u8996\u7a97\u5f8c\u91cd\u65b0\u4e26\u6392");
                    return;
                }
                StopCleanup(app, _cleanRounds == 0 ? "\u6c92\u6709\u591a\u7684\u8996\u7a97" : "\u591a\u7684\u8996\u7a97\u5df2\u95dc\u9589\u4e26\u91cd\u65b0\u4e26\u6392");
            }
            catch (Exception ex)
            {
                Log("\u9592\u7f6e\u6536\u5c3e\u767c\u751f\u932f\u8aa4\uff1a" + ex.Message);
                StopCleanup(app, "\u767c\u751f\u932f\u8aa4");
            }
        }

        // 標準模型、學生模型合計多出幾個視窗（各應只有 1 個）
        private int ExtraWindowCount()
        {
            int n = 0;
            if (_standardDoc != null && _standardDoc.IsValidObject)
                n += Math.Max(0, new UIDocument(_standardDoc).GetOpenUIViews().Count - 1);
            if (_studentDoc != null && _studentDoc.IsValidObject)
                n += Math.Max(0, new UIDocument(_studentDoc).GetOpenUIViews().Count - 1);
            return n;
        }

        private void CloseExtraWindows()
        {
            if (_standardDoc != null && _standardDoc.IsValidObject && _stdView != null && _stdView.IsValidObject)
                KeepSingleWindow(new UIDocument(_standardDoc), _stdView);
            if (_studentDoc != null && _studentDoc.IsValidObject && _stuView != null && _stuView.IsValidObject)
                KeepSingleWindow(new UIDocument(_studentDoc), _stuView);
        }

        // 一個文件只留一個視窗：保留指定視圖的第一個視窗，其他（重複的、別的視圖）都關掉
        private static void KeepSingleWindow(UIDocument uiDoc, View keep)
        {
            if (uiDoc == null || keep == null) return;
            bool kept = false;
            foreach (var uv in uiDoc.GetOpenUIViews())
            {
                string name = uiDoc.Document.GetElement(uv.ViewId)?.Name ?? uv.ViewId.ToString();
                if (!kept && uv.ViewId == keep.Id) { kept = true; continue; }
                try
                {
                    uv.Close();
                    Log($"  \u95dc\u9589 {uiDoc.Document.Title} \u7684\u8996\u7a97\u300c{name}\u300d");
                }
                catch (Exception ex)
                {
                    Log($"  \u7121\u6cd5\u95dc\u9589 {uiDoc.Document.Title} \u7684\u8996\u7a97\u300c{name}\u300d\uff1a{ex.Message}");
                }
            }
        }

        // ---- 視窗紀錄（%TEMP%\BIMGrading_視窗紀錄.txt）：標色時每一步開了哪些視窗，方便找出多的視窗從哪來 ----
        private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "BIMGrading_\u8996\u7a97\u7d00\u9304.txt");

        private static void ResetLog()
        {
            try { File.WriteAllText(LogPath, $"=== \u6a19\u8272 {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===\r\n"); } catch { }
        }

        internal static void Log(string line)
        {
            try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}\r\n"); } catch { }
        }

        private static void LogWindows(UIApplication app, string label)
        {
            try
            {
                Log(label + "\uff1a");
                foreach (Document doc in app.Application.Documents)
                {
                    if (doc.IsLinked) continue;
                    var views = new UIDocument(doc).GetOpenUIViews();
                    string names = string.Join("\u3001", views.Select(v => doc.GetElement(v.ViewId)?.Name ?? v.ViewId.ToString()));
                    bool isActive = app.ActiveUIDocument != null && app.ActiveUIDocument.Document.Equals(doc);
                    Log($"  {doc.Title}{(isActive ? "（作用中）" : "")}\uff1a{views.Count} \u500b\u8996\u7a97 [{names}]");
                }
            }
            catch (Exception ex)
            {
                Log("  \uff08\u8b80\u53d6\u8996\u7a97\u5931\u6557\uff1a" + ex.Message + "\uff09");
            }
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