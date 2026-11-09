using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using System.IO;
using System.Windows.Forms;

namespace RevitGradingTool
{
    [Transaction(TransactionMode.Manual)]
    public class BatchExportCommand : IExternalCommand
    {
        public Result Execute(
            ExternalCommandData commandData,
            ref string message,
            ElementSet elements)
        {
            // 選學生 .rvt 所在資料夾
            var folderDlg = new FolderBrowserDialog
            {
                Description = "\u9078\u64c7\u5b58\u653e\u6240\u6709\u5b78\u751f .rvt \u7684\u8cc7\u6599\u593e"
            };
            if (folderDlg.ShowDialog() != DialogResult.OK) return Result.Cancelled;

            // 選 JSON 輸出資料夾
            var outputDlg = new FolderBrowserDialog
            {
                Description = "\u9078\u64c7 JSON \u8f38\u51fa\u8cc7\u6599\u593e"
            };
            if (outputDlg.ShowDialog() != DialogResult.OK) return Result.Cancelled;

            string inputFolder = folderDlg.SelectedPath;
            string outputFolder = outputDlg.SelectedPath;

            var rvtFiles = Directory.GetFiles(inputFolder, "*.rvt");
            if (rvtFiles.Length == 0)
            {
                TaskDialog.Show("\u932f\u8aa4", "\u8cc7\u6599\u593e\u5167\u6c92\u6709 .rvt \u6a94\u6848");
                return Result.Failed;
            }

            var app = commandData.Application;
            int count = 0;

            foreach (string rvtPath in rvtFiles)
            {
                Document studentDoc = null;
                try
                {
                    var openOptions = new OpenOptions
                    {
                        DetachFromCentralOption = DetachFromCentralOption.DetachAndDiscardWorksets
                    };
                    studentDoc = app.Application.OpenDocumentFile(
                        ModelPathUtils.ConvertUserVisiblePathToModelPath(rvtPath),
                        openOptions);
                }
                catch { continue; }

                var snapshot = ExportSnapshot.ParseDocument(studentDoc);

                string jsonPath = Path.Combine(outputFolder,
                    Path.GetFileNameWithoutExtension(rvtPath) + "_snapshot.json");
                File.WriteAllText(jsonPath,
                    JsonConvert.SerializeObject(snapshot, Newtonsoft.Json.Formatting.Indented));

                studentDoc.Close(false);
                count++;
            }

            TaskDialog.Show("\u6279\u6b21\u532f\u51fa\u5b8c\u6210",
                $"\u5171\u8655\u7406 {count} \u500b\u5b78\u751f\u6a21\u578b\nJSON \u5df2\u5b58\u81f3\uff1a{outputFolder}");

            return Result.Succeeded;
        }
    }
}