using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RevitGradingTool
{
    // 自動匯入：開啟標準模型與助教選的學生 .rvt，直接轉成快照交給面板比對
    // （面板是非強制回應視窗，開檔必須透過 ExternalEvent 在 Revit API 環境中執行）
    public class AutoImportEvent : IExternalEventHandler
    {
        public string StandardRvtPath { get; set; }
        public string[] StudentRvtPaths { get; set; }
        public TaPanel Panel { get; set; }

        public void Execute(UIApplication app)
        {
            if (Panel == null) return;
            try
            {
                RunImport(app);
            }
            catch (Exception ex)
            {
                Panel.ShowProgress("");
                TaskDialog.Show("匯入失敗", ex.Message);
            }
            finally
            {
                // 不論成功或中途失敗，都要讓按鈕恢復可按
                Panel.EnableImportButtons();
            }
        }

        private void RunImport(UIApplication app)
        {
            if (Panel == null || string.IsNullOrEmpty(StandardRvtPath) || StudentRvtPaths == null)
                return;

            // 1. 標準模型：開啟並設為作用中文件（標色功能以作用中文件當作標準模型）
            Document stdDoc;
            try
            {
                var active = app.ActiveUIDocument?.Document;
                if (active != null && SamePath(active.PathName, StandardRvtPath))
                    stdDoc = active;
                else
                {
                    Panel.ShowProgress("開啟標準模型中…");
                    stdDoc = app.OpenAndActivateDocument(StandardRvtPath).Document;
                }
            }
            catch (Exception ex)
            {
                Panel.ShowProgress("");
                TaskDialog.Show("錯誤", $"無法開啟標準模型：{StandardRvtPath}\n{ex.Message}");
                return;
            }

            var standard = ExportSnapshot.ParseDocument(stdDoc);
            Panel.SetStandard(standard, Path.GetFileName(StandardRvtPath));

            // 2. 學生模型：助教選的檔案，排除誤選到的標準模型本身
            var rvtFiles = StudentRvtPaths
                .Where(f => !SamePath(f, StandardRvtPath))
                .OrderBy(f => f)
                .ToList();

            if (rvtFiles.Count == 0)
            {
                Panel.ShowProgress("");
                TaskDialog.Show("錯誤", "沒有選擇學生模型（選到的檔案只有標準模型本身）");
                return;
            }

            var results = new List<StudentResult>();
            var failed = new List<string>();

            for (int i = 0; i < rvtFiles.Count; i++)
            {
                string path = rvtFiles[i];
                string name = Path.GetFileNameWithoutExtension(path);
                Panel.ShowProgress($"轉換中 {i + 1}/{rvtFiles.Count}：{name}");

                // 已經開著的檔案（例如上次標色開的學生模型）不要幫使用者關掉
                bool wasOpen = app.Application.Documents.Cast<Document>()
                    .Any(d => SamePath(d.PathName, path));

                Document doc = null;
                try
                {
                    var openOptions = new OpenOptions
                    {
                        DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
                    };
                    doc = app.Application.OpenDocumentFile(
                        ModelPathUtils.ConvertUserVisiblePathToModelPath(path), openOptions);

                    results.Add(new StudentResult
                    {
                        Name = name,
                        RvtPath = path,
                        Snapshot = ExportSnapshot.ParseDocument(doc)
                    });
                }
                catch (Exception ex)
                {
                    failed.Add($"{name}：{ex.Message}");
                }
                finally
                {
                    if (doc != null && !wasOpen && !doc.Equals(stdDoc))
                    {
                        try { doc.Close(false); } catch { }
                    }
                }
            }

            Panel.SetStudents(results);

            // 測試模型（資料夾裡有 pyRevit 產生的答案表）：自動對照答案表、匯出快照與驗證報告
            try
            {
                Panel.ShowProgress("自動驗證中…");
                string summary = TestValidation.Run(StandardRvtPath, standard, results);
                Panel.ShowProgress(summary ?? $"完成：共比對 {results.Count} 位學生，點選查看錯誤");
            }
            catch (Exception ex)
            {
                Panel.ShowProgress($"完成：共比對 {results.Count} 位學生（自動驗證失敗：{ex.Message}）");
            }

            if (failed.Count > 0)
                TaskDialog.Show("部分檔案無法開啟",
                    $"共 {failed.Count} 個檔案略過：\n" + string.Join("\n", failed));
        }

        private static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        public string GetName() => "AutoImportEvent";
    }
}