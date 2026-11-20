using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RevitGradingTool
{
    public class StudentResult
    {
        public string Name { get; set; }
        public string SnapshotPath { get; set; }
        public string RvtPath { get; set; }
        public List<CompareError> Errors { get; set; } = new List<CompareError>();
    }

    public class TaPanel : Form
    {
        private ListBox studentListBox;
        private Label errorCountLabel;
        private Button loadStudentsBtn;
        private Button loadStandardBtn;
        private RichTextBox errorDetailBox;
        private Button highlightBtn;

        public ModelSnapshot StandardSnapshot { get; private set; }
        public List<StudentResult> Students { get; private set; } = new List<StudentResult>();
        public StudentResult SelectedStudent { get; private set; }

        public event EventHandler<StudentResult> HighlightRequested;
        public event EventHandler ExportRequested;
        public event EventHandler ClearRequested;

        public TaPanel()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "BIM \u4f5c\u696d\u6279\u6539\u7cfb\u7d71";
            this.Width = 420;
            this.Height = 600;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.TopMost = true;

            loadStandardBtn = new Button
            {
                Text = "\u8f09\u5165\u6a19\u6e96\u6a21\u578b JSON",
                Dock = DockStyle.Top,
                Height = 36
            };
            loadStandardBtn.Click += LoadStandardBtn_Click;

            loadStudentsBtn = new Button
            {
                Text = "\u6279\u6b21\u8f09\u5165\u5b78\u751f JSON",
                Dock = DockStyle.Top,
                Height = 36,
                Enabled = false
            };
            loadStudentsBtn.Click += LoadStudentsBtn_Click;

            var listLabel = new Label
            {
                Text = "\u5b78\u751f\u6e05\u55ae\uff08\u9ede\u9078\u67e5\u770b\u932f\u8aa4\uff09",
                Dock = DockStyle.Top,
                Height = 20
            };

            studentListBox = new ListBox
            {
                Dock = DockStyle.Top,
                Height = 180,
                Font = new System.Drawing.Font("Microsoft JhengHei", 10)
            };
            studentListBox.SelectedIndexChanged += StudentListBox_SelectedIndexChanged;

            errorCountLabel = new Label
            {
                Text = "\u9078\u64c7\u5b78\u751f\u4ee5\u67e5\u770b\u932f\u8aa4",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new System.Drawing.Font("Microsoft JhengHei", 9)
            };

            highlightBtn = new Button
            {
                Text = "\u5728\u6a21\u578b\u4e0a\u6a19\u8272\u6b64\u5b78\u751f\u7684\u932f\u8aa4",
                Dock = DockStyle.Top,
                Height = 36,
                Enabled = false,
                BackColor = System.Drawing.Color.LightCoral
            };
            highlightBtn.Click += HighlightBtn_Click;

            var detailLabel = new Label
            {
                Text = "\u932f\u8aa4\u8a73\u7d30\uff1a",
                Dock = DockStyle.Top,
                Height = 20
            };

            errorDetailBox = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                Font = new System.Drawing.Font("Microsoft JhengHei", 9),
                BackColor = System.Drawing.Color.WhiteSmoke
            };

            var exportBtn = new Button
            {
                Text = "\u532f\u51fa Excel \u5831\u544a",
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = System.Drawing.Color.LightGreen
            };
            exportBtn.Click += (s, e) => ExportRequested?.Invoke(this, e);

            var clearBtn = new Button
            {
                Text = "\u6e05\u9664\u6a19\u8272",
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = System.Drawing.Color.LightGray
            };
            clearBtn.Click += (s, e) => ClearRequested?.Invoke(this, e);

            // 由下往上加（Dock.Top 是反序）
            this.Controls.Add(errorDetailBox);
            this.Controls.Add(detailLabel);
            this.Controls.Add(exportBtn);
            this.Controls.Add(clearBtn);
            this.Controls.Add(highlightBtn);
            this.Controls.Add(errorCountLabel);
            this.Controls.Add(studentListBox);
            this.Controls.Add(listLabel);
            this.Controls.Add(loadStudentsBtn);
            this.Controls.Add(loadStandardBtn);
        }

        private void LoadStandardBtn_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "\u9078\u64c7\u6a19\u6e96\u6a21\u578b JSON",
                Filter = "JSON files|*.json"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            StandardSnapshot = Newtonsoft.Json.JsonConvert.DeserializeObject<ModelSnapshot>(
                System.IO.File.ReadAllText(dlg.FileName));

            loadStandardBtn.Text = $"\u6a19\u6e96\u6a21\u578b\uff1a{System.IO.Path.GetFileName(dlg.FileName)}";
            loadStudentsBtn.Enabled = true;
        }

        private void LoadStudentsBtn_Click(object sender, EventArgs e)
        {
            // 先選 .rvt 資料夾
            var rvtFolderDlg = new FolderBrowserDialog
            {
                Description = "\u9078\u64c7\u5b58\u653e\u5b78\u751f .rvt \u7684\u8cc7\u6599\u593e\uff08\u53ef\u4e0d\u9078\uff09"
            };
            string rvtFolder = "";
            if (rvtFolderDlg.ShowDialog() == DialogResult.OK)
                rvtFolder = rvtFolderDlg.SelectedPath;

            // 再選 JSON 檔案
            var dlg = new OpenFileDialog
            {
                Title = "\u9078\u64c7\u5b78\u751f JSON\uff08\u53ef\u591a\u9078\uff09",
                Filter = "JSON files|*.json",
                Multiselect = true
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            Students.Clear();
            studentListBox.Items.Clear();

            var engine = new ComparisonEngine();

            foreach (var path in dlg.FileNames)
            {
                var studentSnap = Newtonsoft.Json.JsonConvert.DeserializeObject<ModelSnapshot>(
                    System.IO.File.ReadAllText(path));

                var errors = engine.Compare(StandardSnapshot, studentSnap);
                var result = new StudentResult
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(path),
                    SnapshotPath = path,
                    Errors = errors
                };

                // 自動對應 .rvt 路徑
                if (!string.IsNullOrEmpty(rvtFolder))
                {
                    string baseName = result.Name.Replace("_snapshot", "");
                    string rvtPath = System.IO.Path.Combine(rvtFolder, baseName + ".rvt");
                    if (System.IO.File.Exists(rvtPath))
                        result.RvtPath = rvtPath;
                    else
                        System.Windows.Forms.MessageBox.Show($"找不到：\n{rvtPath}");
                }

                Students.Add(result);
                studentListBox.Items.Add($"{result.Name}　　\u932f\u8aa4\uff1a{errors.Count} \u500b");
            }
        }

        private void StudentListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = studentListBox.SelectedIndex;
            if (idx < 0 || idx >= Students.Count) return;

            SelectedStudent = Students[idx];
            errorCountLabel.Text = $"\u3010{SelectedStudent.Name}\u3011\u5171 {SelectedStudent.Errors.Count} \u500b\u932f\u8aa4";
            highlightBtn.Enabled = true;

            errorDetailBox.Clear();
            foreach (var err in SelectedStudent.Errors)
            {
                string icon = err.Type == ErrorType.Missing ? "[\u7f3a\u5c11]    " :
                              err.Type == ErrorType.Extra ? "[\u591a\u9918]    " :
                              err.Type == ErrorType.WrongRotation ? "[\u65cb\u8f49\u932f\u8aa4]" :
                              err.Type == ErrorType.WrongPosition ? "[\u4f4d\u7f6e\u504f\u79fb]" :
                                                                   "[\u5c6c\u6027\u932f\u8aa4]";
                errorDetailBox.AppendText($"{icon} {err.Category}\n");
                errorDetailBox.AppendText($"  {err.Detail}\n");
                errorDetailBox.AppendText($"  \u4f4d\u7f6e\uff1aX={err.X:F0}, Y={err.Y:F0}, Z={err.Z:F0}\n\n");
            }
        }

        private void HighlightBtn_Click(object sender, EventArgs e)
        {
            if (SelectedStudent != null)
                HighlightRequested?.Invoke(this, SelectedStudent);
        }
    }
}