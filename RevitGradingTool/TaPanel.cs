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

        // 各樓層的比對結果快取：切換樓層時不用重新比對（全部樓層的模型很大時要算很久）
        public Dictionary<string, List<CompareError>> ErrorsByFloor { get; } = new Dictionary<string, List<CompareError>>();
    }

    public class TaPanel : Form
    {
        private ListBox studentListBox;
        private Label errorCountLabel;
        private Button loadStudentsBtn;
        private Button loadStandardBtn;
        private RichTextBox errorDetailBox;
        private Button highlightBtn;
        private Button focusBtn;
        private Button nextBtn;
        private int currentErrorIndex = -1;                            // 目前查看的是第幾個錯誤
        private readonly List<int> errorTextStarts = new List<int>();  // 每個錯誤在詳細框中的起始字元位置
        private ComboBox floorCombo;
        private ComboBox groupCombo;

        public ModelSnapshot StandardSnapshot { get; private set; }
        public List<StudentResult> Students { get; private set; } = new List<StudentResult>();
        public StudentResult SelectedStudent { get; private set; }
        public string SelectedFloor => floorCombo?.SelectedItem?.ToString() ?? "\u5168\u90e8\u6a13\u5c64";
        public string SelectedGroup => groupCombo?.SelectedItem?.ToString() ?? ComparisonEngine.ALL_GROUPS;
        public Dictionary<string, (double MinZ, double MaxZ)> FloorRanges { get; private set; } = new Dictionary<string, (double, double)>();

        public event EventHandler<StudentResult> HighlightRequested;
        public event EventHandler ExportRequested;
        public event EventHandler ClearRequested;

        // 放大到某個錯誤：學生、錯誤索引（由 TaPanelCommand 交給 HighlightEvent 在 Revit 中執行）
        public event Action<StudentResult, int> FocusRequested;

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
            this.Height = 750;

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

            // 構件類別篩選：只看結構、建築、樓梯扶手、設備家具…（比對一次，切換只是篩選顯示）
            var groupLabel = new Label
            {
                Text = "\u69cb\u4ef6\u985e\u5225\uff1a",
                Dock = DockStyle.Top,
                Height = 20
            };

            groupCombo = new ComboBox
            {
                Dock = DockStyle.Top,
                Height = 30,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            groupCombo.Items.Add(ComparisonEngine.ALL_GROUPS);
            foreach (var g in ComparisonEngine.Groups)
                groupCombo.Items.Add(g);
            groupCombo.SelectedIndex = 0;
            groupCombo.SelectedIndexChanged += (s, e) => RefreshStudentList();

            // 樓層範圍、構件類別並排，上下留空間
            foreach (Control c in new Control[] { floorLabel, floorCombo, groupLabel, groupCombo })
            {
                c.Dock = DockStyle.Fill;
                c.Margin = new Padding(0, 0, 8, 0);
            }
            floorLabel.TextAlign = System.Drawing.ContentAlignment.BottomLeft;
            groupLabel.TextAlign = System.Drawing.ContentAlignment.BottomLeft;

            var filterPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 78,
                ColumnCount = 2,
                RowCount = 2,
                Padding = new Padding(6, 12, 0, 12)
            };
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            filterPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            filterPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
            filterPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            filterPanel.Controls.Add(floorLabel, 0, 0);
            filterPanel.Controls.Add(groupLabel, 1, 0);
            filterPanel.Controls.Add(floorCombo, 0, 1);
            filterPanel.Controls.Add(groupCombo, 1, 1);

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

            // 錯誤導覽：放大到目前錯誤 / 跳到下一個錯誤
            focusBtn = new Button
            {
                Text = "\u986f\u793a\u76ee\u524d\u932f\u8aa4",
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Enabled = false,
                BackColor = System.Drawing.Color.LightSkyBlue
            };
            focusBtn.Click += (s, e) => FocusCurrentError();

            nextBtn = new Button
            {
                Text = "\u4e0b\u4e00\u500b\u932f\u8aa4 \u25b6",
                Dock = DockStyle.Fill,
                Margin = new Padding(0),
                Enabled = false,
                BackColor = System.Drawing.Color.LightSkyBlue
            };
            nextBtn.Click += (s, e) =>
            {
                if (SelectedStudent == null || SelectedStudent.Errors.Count == 0) return;
                currentErrorIndex = (currentErrorIndex + 1) % SelectedStudent.Errors.Count;
                FocusCurrentError();
            };

            var navPanel = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                ColumnCount = 2,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            navPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            navPanel.Controls.Add(focusBtn, 0, 0);
            navPanel.Controls.Add(nextBtn, 1, 0);

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
            // 點一下錯誤 = 設為目前錯誤；點兩下 = 直接放大到該處
            errorDetailBox.MouseUp += (s, e) => SelectErrorAtCaret();
            errorDetailBox.DoubleClick += (s, e) => { if (SelectErrorAtCaret()) FocusCurrentError(); };

            var exportBtn = new Button
            {
                Text = "\u532f\u51fa Excel \u5831\u544a",
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = System.Drawing.Color.LightGreen
            };
            exportBtn.Click += (s, e) => ExportRequested?.Invoke(this, e);

            // 除錯用：把標準模型與目前學生的快照存成 JSON，可以拿去離線分析比對結果
            var dumpBtn = new Button
            {
                Text = "\u532f\u51fa\u6bd4\u5c0d\u8cc7\u6599\uff08JSON\uff0c\u9664\u932f\u7528\uff09",
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = System.Drawing.Color.Gainsboro
            };
            dumpBtn.Click += (s, e) => DumpSnapshots();

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
            this.Controls.Add(dumpBtn);
            this.Controls.Add(exportBtn);
            this.Controls.Add(clearBtn);
            this.Controls.Add(navPanel);
            this.Controls.Add(highlightBtn);
            this.Controls.Add(errorCountLabel);
            this.Controls.Add(studentListBox);
            this.Controls.Add(listLabel);
            this.Controls.Add(filterPanel);
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
            foreach (var s in Students) s.ErrorsByFloor.Clear();  // 標準模型換了，舊的比對結果作廢

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
            focusBtn.Enabled = false;
            nextBtn.Enabled = false;
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
            int selected = studentListBox.SelectedIndex;
            studentListBox.Items.Clear();
            if (StandardSnapshot == null || Students.Count == 0) return;

            var engine = new ComparisonEngine();
            string floor = SelectedFloor;
            string group = SelectedGroup;

            for (int i = 0; i < Students.Count; i++)
            {
                List<CompareError> errors;
                if (!Students[i].ErrorsByFloor.TryGetValue(floor, out errors))
                {
                    errors = engine.Compare(StandardSnapshot, Students[i].Snapshot, floor, FloorRanges);
                    Students[i].ErrorsByFloor[floor] = errors;
                }
                if (group != ComparisonEngine.ALL_GROUPS)
                    errors = errors.FindAll(e => e.Group == group);
                Students[i].Errors = errors;

                studentListBox.Items.Add($"{Students[i].Name}\u3000\u3000\u932f\u8aa4\uff1a{errors.Count} \u500b");
            }

            // 切換樓層或類別後保留原本選的學生，錯誤詳細會自動換成篩選後的結果
            if (selected >= 0 && selected < studentListBox.Items.Count)
                studentListBox.SelectedIndex = selected;
        }


        private void StudentListBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            int idx = studentListBox.SelectedIndex;
            if (idx < 0 || idx >= Students.Count) return;

            SelectedStudent = Students[idx];
            highlightBtn.Enabled = true;
            bool hasErrors = SelectedStudent.Errors.Count > 0;
            focusBtn.Enabled = hasErrors;
            nextBtn.Enabled = hasErrors;
            currentErrorIndex = hasErrors ? 0 : -1;

            errorDetailBox.Clear();
            errorTextStarts.Clear();
            for (int i = 0; i < SelectedStudent.Errors.Count; i++)
            {
                var err = SelectedStudent.Errors[i];
                string icon = err.Type == ErrorType.Missing ? "[\u7f3a\u5c11]    " :
                              err.Type == ErrorType.Extra ? "[\u591a\u9918]    " :
                              err.Type == ErrorType.WrongRotation ? "[\u65cb\u8f49\u932f\u8aa4]" :
                              err.Type == ErrorType.WrongPosition ? "[\u4f4d\u7f6e\u504f\u79fb]" :
                                                                    "[\u5c6c\u6027\u932f\u8aa4]";
                string floor = string.IsNullOrEmpty(err.Level) ? "\u672a\u77e5" : err.Level;
                errorTextStarts.Add(errorDetailBox.TextLength);
                // 元件 ID：缺少只有標準模型的、多餘只有學生模型的，其他兩邊都有
                string ids = err.Type == ErrorType.Missing ? $"\u6a19\u6e96 {err.StdElementId}" :
                             err.Type == ErrorType.Extra ? $"\u5b78\u751f {err.StuElementId}" :
                                                           $"\u6a19\u6e96 {err.StdElementId} \u2192 \u5b78\u751f {err.StuElementId}";
                errorDetailBox.AppendText($"{i + 1}. {icon} {err.Category}\u3000\u6a13\u5c64\uff1a{floor}\u3000\u5143\u4ef6ID\uff1a{ids}\n");
                errorDetailBox.AppendText($"  {err.Detail}\n\n");
            }
            errorTextStarts.Add(errorDetailBox.TextLength);
            MarkCurrentError();
        }

        // 在詳細框中把目前錯誤標成灰底並捲到可見位置，標題列顯示「目前第 N 個」
        private void MarkCurrentError()
        {
            if (SelectedStudent == null) return;
            int count = SelectedStudent.Errors.Count;
            errorCountLabel.Text = count == 0
                ? $"\u3010{SelectedStudent.Name}\u3011\u6c92\u6709\u932f\u8aa4"
                : $"\u3010{SelectedStudent.Name}\u3011\u5171 {count} \u500b\u932f\u8aa4\u3000\u76ee\u524d\u7b2c {currentErrorIndex + 1} \u500b";

            errorDetailBox.SelectAll();
            errorDetailBox.SelectionBackColor = errorDetailBox.BackColor;
            if (currentErrorIndex >= 0 && currentErrorIndex + 1 < errorTextStarts.Count)
            {
                int start = errorTextStarts[currentErrorIndex];
                errorDetailBox.Select(start, errorTextStarts[currentErrorIndex + 1] - start);
                errorDetailBox.SelectionBackColor = System.Drawing.Color.LightGray;
                errorDetailBox.Select(start, 0);
                errorDetailBox.ScrollToCaret();
            }
            else
            {
                errorDetailBox.Select(0, 0);
            }
        }

        // 依游標位置找出點到的是第幾個錯誤
        private bool SelectErrorAtCaret()
        {
            int pos = errorDetailBox.SelectionStart;
            for (int i = 0; i + 1 < errorTextStarts.Count; i++)
            {
                if (pos >= errorTextStarts[i] && pos < errorTextStarts[i + 1])
                {
                    currentErrorIndex = i;
                    MarkCurrentError();
                    return true;
                }
            }
            return false;
        }

        private void FocusCurrentError()
        {
            if (SelectedStudent == null || SelectedStudent.Errors.Count == 0) return;
            if (currentErrorIndex < 0 || currentErrorIndex >= SelectedStudent.Errors.Count) currentErrorIndex = 0;
            MarkCurrentError();
            FocusRequested?.Invoke(SelectedStudent, currentErrorIndex);
        }

        private void DumpSnapshots()
        {
            if (StandardSnapshot == null || SelectedStudent?.Snapshot == null)
            {
                MessageBox.Show(this, "\u8acb\u5148\u8f09\u5165\u6a21\u578b\u4e26\u9ede\u9078\u4e00\u4f4d\u5b78\u751f", "\u532f\u51fa\u6bd4\u5c0d\u8cc7\u6599");
                return;
            }
            var dlg = new FolderBrowserDialog { Description = "\u9078\u64c7\u5b58\u653e\u6bd4\u5c0d\u8cc7\u6599\u7684\u8cc7\u6599\u593e" };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;

            string Save(string name, ModelSnapshot snap)
            {
                foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
                string path = System.IO.Path.Combine(dlg.SelectedPath, name + "_snapshot.json");
                System.IO.File.WriteAllText(path,
                    Newtonsoft.Json.JsonConvert.SerializeObject(snap, Newtonsoft.Json.Formatting.Indented));
                return path;
            }

            string p1 = Save(StandardSnapshot.ModelName ?? "\u6a19\u6e96\u6a21\u578b", StandardSnapshot);
            string p2 = Save(SelectedStudent.Name, SelectedStudent.Snapshot);
            MessageBox.Show(this, $"\u5df2\u532f\u51fa\uff1a\n{p1}\n{p2}", "\u532f\u51fa\u6bd4\u5c0d\u8cc7\u6599");
        }

        private void HighlightBtn_Click(object sender, EventArgs e)

        {
            if (SelectedStudent != null)
                HighlightRequested?.Invoke(this, SelectedStudent);
        }
    }
}