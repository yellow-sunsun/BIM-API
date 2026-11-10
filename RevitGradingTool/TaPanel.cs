using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace RevitGradingTool
{
    public class StudentResult
    {
        public string Name { get; set; }
        public string SnapshotPath { get; set; }
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

        // 當助教點「標色」時觸發
        public event EventHandler<StudentResult> HighlightRequested;

        public event EventHandler ExportRequested;

        public TaPanel()
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "BIM 作業批改系統";
            this.Width = 420;
            this.Height = 600;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.TopMost = true;

            // 載入標準模型按鈕
            loadStandardBtn = new Button
            {
                Text = "載入標準模型 JSON",
                Dock = DockStyle.Top,
                Height = 36
            };
            loadStandardBtn.Click += LoadStandardBtn_Click;

            // 批次載入學生 JSON 按鈕
            loadStudentsBtn = new Button
            {
                Text = "批次載入學生 JSON",
                Dock = DockStyle.Top,
                Height = 36,
                Enabled = false
            };
            loadStudentsBtn.Click += LoadStudentsBtn_Click;

            // 學生清單
            var listLabel = new Label
            {
                Text = "學生清單（點選查看錯誤）",
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

            // 錯誤數量標籤
            errorCountLabel = new Label
            {
                Text = "選擇學生以查看錯誤",
                Dock = DockStyle.Top,
                Height = 24,
                Font = new System.Drawing.Font("Microsoft JhengHei", 9)
            };

            // 標色按鈕
            highlightBtn = new Button
            {
                Text = "在模型上標色此學生的錯誤",
                Dock = DockStyle.Top,
                Height = 36,
                Enabled = false,
                BackColor = System.Drawing.Color.LightCoral
            };
            highlightBtn.Click += HighlightBtn_Click;

            // 錯誤詳細清單
            var detailLabel = new Label
            {
                Text = "錯誤詳細：",
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

            // 由下往上加（Dock.Top 是反序）
            this.Controls.Add(errorDetailBox);
            this.Controls.Add(detailLabel);
            this.Controls.Add(highlightBtn);
            this.Controls.Add(errorCountLabel);
            this.Controls.Add(studentListBox);
            this.Controls.Add(listLabel);
            this.Controls.Add(loadStudentsBtn);
            // Excel 匯出按鈕
            var exportBtn = new Button
            {
                Text = "匯出 Excel 報告",
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = System.Drawing.Color.LightGreen
            };
            exportBtn.Click += (s, e) => ExportRequested?.Invoke(this, e);
            this.Controls.Add(exportBtn);
            this.Controls.Add(loadStandardBtn);
        }

        private void LoadStandardBtn_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "選擇標準模型 JSON",
                Filter = "JSON files|*.json"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;

            StandardSnapshot = Newtonsoft.Json.JsonConvert.DeserializeObject<ModelSnapshot>(
                System.IO.File.ReadAllText(dlg.FileName));

            loadStandardBtn.Text = $"標準模型：{System.IO.Path.GetFileName(dlg.FileName)}";
            loadStudentsBtn.Enabled = true;
        }

        private void LoadStudentsBtn_Click(object sender, EventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "選擇學生 JSON（可多選）",
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

                Students.Add(result);
                studentListBox.Items.Add($"{result.Name}　　錯誤：{errors.Count} 個");
            }
        }

        private void StudentListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = studentListBox.SelectedIndex;
            if (idx < 0 || idx >= Students.Count) return;

            SelectedStudent = Students[idx];
            errorCountLabel.Text = $"【{SelectedStudent.Name}】共 {SelectedStudent.Errors.Count} 個錯誤";
            highlightBtn.Enabled = true;

            errorDetailBox.Clear();
            foreach (var err in SelectedStudent.Errors)
            {
                string icon = err.Type == ErrorType.Missing ? "[缺少]    " :
                              err.Type == ErrorType.Extra ? "[多餘]    " : "[屬性錯誤]";
                errorDetailBox.AppendText($"{icon} {err.Category}\n");
                errorDetailBox.AppendText($"  {err.Detail}\n");
                errorDetailBox.AppendText($"  位置：X={err.X:F0}, Y={err.Y:F0}, Z={err.Z:F0}\n\n");
            }
        }

        private void HighlightBtn_Click(object sender, EventArgs e)
        {
            if (SelectedStudent != null)
                HighlightRequested?.Invoke(this, SelectedStudent);
        }
    }
}
