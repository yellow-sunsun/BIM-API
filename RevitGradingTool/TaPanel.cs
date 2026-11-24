using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RevitGradingTool
{
    public class StudentResult
    {
        public string Name { get; set; }
        public string RvtPath { get; set; }
        public ModelSnapshot Snapshot { get; set; }  // 從 .rvt 直接解析的快照（不必再存成 JSON）
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
        private ComboBox floorCombo;

        public ModelSnapshot StandardSnapshot { get; private set; }
        public List<StudentResult> Students { get; private set; } = new List<StudentResult>();
        public StudentResult SelectedStudent { get; private set; }
        public string SelectedFloor => floorCombo?.SelectedItem?.ToString() ?? "\u5168\u90e8\u6a13\u5c64";
        public Dictionary<string, (double MinZ, double MaxZ)> FloorRanges { get; private set; } = new Dictionary<string, (double, double)>();

        public event EventHandler<StudentResult> HighlightRequested;
        public event EventHandler ExportRequested;
        public event EventHandler ClearRequested;

        // 使用者選好標準模型 .rvt 與學生模型後觸發，由 TaPanelCommand 交給 AutoImportEvent 開檔轉換
        public event Action<string, string[]> ImportRequested;
        private string standardRvtPath;

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
                Text = "\u9078\u64c7\u6a19\u6e96\u6a21\u578b\uff08.rvt\uff09",
                Dock = DockStyle.Top,
                Height = 36
            };
            loadStandardBtn.Click += LoadStandardBtn_Click;

            loadStudentsBtn = new Button
            {
                Text = "\u9078\u64c7\u5b78\u751f\u6a21\u578b(.rvt)",
                Dock = DockStyle.Top,
                Height = 36,
                Enabled = false
            };
            loadStudentsBtn.Click += LoadStudentsBtn_Click;

            var floorLabel = new Label
            {
                Text = "\u6a13\u5c64\u7bc4\u570d\uff1a",
                Dock = DockStyle.Top,
                Height = 20
            };

            floorCombo = new ComboBox
            {
                Dock = DockStyle.Top,
                Height = 30,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            floorCombo.Items.Add("\u5168\u90e8\u6a13\u5c64");
            floorCombo.SelectedIndex = 0;
            floorCombo.SelectedIndexChanged += (s, e) => RefreshStudentList();

            var listLabel = new Label
            {
                Text = "\u5b78\u751f\u6e05\u55ae\uff08\u9ede\u9078\u67e5\u770b\u932f\u8aa4\uff09",
                Dock = DockStyle.Top,
                Height = 32,
                TextAlign = System.Drawing.ContentAlignment.BottomLeft
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

            this.Controls.Add(errorDetailBox);
            this.Controls.Add(detailLabel);
            this.Controls.Add(exportBtn);
            this.Controls.Add(clearBtn);
            this.Controls.Add(highlightBtn);
            this.Controls.Add(errorCountLabel);
            this.Controls.Add(studentListBox);
            this.Controls.Add(listLabel);
            this.Controls.Add(floorCombo);
            this.Controls.Add(floorLabel);
            this.Controls.Add(loadStudentsBtn);
            this.Controls.Add(loadStandardBtn);
        }

        private void LoadStandardBtn_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "\u9078\u64c7\u6a19\u6e96\u6a21\u578b .rvt",
                Filter = "Revit files|*.rvt"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            // 只記下路徑；實際開檔解析要等選完學生資料夾，在 Revit API 環境中一起執行
            standardRvtPath = dlg.FileName;
            loadStandardBtn.Text = $"\u6a19\u6e96\u6a21\u578b\uff1a{System.IO.Path.GetFileName(standardRvtPath)}";
            loadStudentsBtn.Enabled = true;
        }

        private void LoadStudentsBtn_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(standardRvtPath)) return;

            var dlg = new OpenFileDialog
            {
                Title = "\u9078\u64c7\u5b78\u751f\u6a21\u578b(\u53ef\u8907\u9078)",
                Filter = "Revit files|*.rvt",
                Multiselect = true
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            loadStandardBtn.Enabled = false;
            loadStudentsBtn.Enabled = false;
            ShowProgress("\u6e96\u5099\u958b\u555f\u6a21\u578b\u2026");
            ImportRequested?.Invoke(standardRvtPath, dlg.FileNames);

        }

        // 以下由 AutoImportEvent 在 Revit 主執行緒呼叫

        public void ShowProgress(string text)
        {
            errorCountLabel.Text = text;
            errorCountLabel.Refresh();
        }

        public void EnableImportButtons()
        {
            loadStandardBtn.Enabled = true;
            loadStudentsBtn.Enabled = !string.IsNullOrEmpty(standardRvtPath);
        }

        public void SetStandard(ModelSnapshot standard, string fileName)
        {
            StandardSnapshot = standard;
            loadStandardBtn.Text = $"\u6a19\u6e96\u6a21\u578b\uff1a{fileName}";

            // 分析樓層
            floorCombo.Items.Clear();
            floorCombo.Items.Add("\u5168\u90e8\u6a13\u5c64");
            FloorRanges = AnalyzeFloors(StandardSnapshot);
            foreach (var floor in FloorRanges.Keys)
                floorCombo.Items.Add(floor);
            floorCombo.SelectedIndex = 0;  // 會觸發 RefreshStudentList，此時學生清單還是舊的
        }

        public void SetStudents(List<StudentResult> students)
        {
            Students = students;
            SelectedStudent = null;
            highlightBtn.Enabled = false;
            errorDetailBox.Clear();

            RefreshStudentList();
            ShowProgress($"\u5b8c\u6210\uff1a\u5171\u6bd4\u5c0d {Students.Count} \u4f4d\u5b78\u751f\uff0c\u9ede\u9078\u67e5\u770b\u932f\u8aa4");
        }

        private Dictionary<string, (double MinZ, double MaxZ)> AnalyzeFloors(ModelSnapshot snapshot)
        {
            var result = new Dictionary<string, (double MinZ, double MaxZ)>();

            foreach (var el in snapshot.Elements)
            {
                if (string.IsNullOrEmpty(el.Level)) continue;
                if (!result.ContainsKey(el.Level))
                    result[el.Level] = (el.Z, el.Z);
                else
                {
                    var r = result[el.Level];
                    result[el.Level] = (Math.Min(r.MinZ, el.Z), Math.Max(r.MaxZ, el.Z));
                }
            }

            // 加上緩衝區
            var keys = new List<string>(result.Keys);
            foreach (var key in keys)
            {
                var r = result[key];
                result[key] = (r.MinZ - 300, r.MaxZ + 300);
            }

            return result;
        }

        private void RefreshStudentList()
        {
            studentListBox.Items.Clear();
            if (StandardSnapshot == null || Students.Count == 0) return;

            var engine = new ComparisonEngine();

            for (int i = 0; i < Students.Count; i++)
            {
                var errors = engine.Compare(StandardSnapshot, Students[i].Snapshot, SelectedFloor, FloorRanges);
                Students[i].Errors = errors;
                studentListBox.Items.Add($"{Students[i].Name}\u3000\u3000\u932f\u8aa4\uff1a{errors.Count} \u500b");
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