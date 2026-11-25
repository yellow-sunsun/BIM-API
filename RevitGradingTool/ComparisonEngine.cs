using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGradingTool
{
    public enum ErrorType
    {
        Missing,
        Extra,
        WrongAttr,
        WrongRotation,
        WrongPosition
    }

    public class CompareError
    {
        public ErrorType Type { get; set; }
        public string Category { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public double StudentX { get; set; }
        public double StudentY { get; set; }
        public double StudentZ { get; set; }
        public string Detail { get; set; }

        public string Level { get; set; }          // 錯誤所在樓層
        public long StdElementId { get; set; }     // 標準模型中的元件 Id（0 = 沒有，例如多餘構件）
        public long StuElementId { get; set; }     // 學生模型中的元件 Id（0 = 沒有，例如缺少構件）
        // 學生模型的原始座標（未經偏移校正），在學生模型裡定位錯誤用
        public double RawStudentX { get; set; }
        public double RawStudentY { get; set; }
        public double RawStudentZ { get; set; }

        public string CategoryKey { get; set; }    // 類別代碼（例如 OST_PlumbingFixtures）
        public string FamilyName { get; set; }
        public string FamilyType { get; set; }
        public string Group { get; set; }          // 類別分組（結構 / 建築 / 樓梯扶手…），面板篩選用

        // 合併成這個錯誤的其他構件（例如一起被移動的牆和門），標色時一併標出
        public List<CompareError> Related { get; set; } = new List<CompareError>();

        public long HostId { get; set; }           // 構件依附的主體 Id（缺少的用標準模型的、多餘的用學生模型的）
        public List<string> DiffKeys { get; set; } = new List<string>();   // 屬性錯誤中不同的項目（類型、級高、扶手高度…）

    }

    public class ComparisonEngine
    {
        public const string ALL_FLOORS = "\u5168\u90e8\u6a13\u5c64";
        public const string ALL_GROUPS = "\u5168\u90e8\u985e\u5225";
        public static readonly string[] Groups = { "\u7d50\u69cb", "\u5efa\u7bc9", "\u6a13\u68af\u6276\u624b", "\u8a2d\u5099\u5bb6\u5177", "\u6a5f\u96fb\u7ba1\u7dda", "\u5176\u4ed6" };

        private class XYZOffset
        {
            public double X, Y, Z;
        }

        public List<CompareError> Compare(ModelSnapshot standard, ModelSnapshot student,
            string selectedFloor = ALL_FLOORS,
            Dictionary<string, (double MinZ, double MaxZ)> floorRanges = null)
        {
            var errors = new List<CompareError>();
            var matched = new HashSet<int>();

            // 樓層篩選
            var stdElements = FilterByFloor(standard.Elements, selectedFloor, floorRanges);
            var stuElements = FilterByFloor(student.Elements, selectedFloor, floorRanges);

            var filteredStd = new ModelSnapshot { ModelName = standard.ModelName, Elements = stdElements };
            var filteredStu = new ModelSnapshot { ModelName = student.ModelName, Elements = stuElements };

            XYZOffset offset = CalcOffset(filteredStd, filteredStu);
            ModelSnapshot correctedStudent = ApplyOffset(filteredStu, offset);

            // 配對分三輪，每一輪對全部構件做完才進下一輪：
            //   1. 族群、類型相同，而且方向相同（線型構件如牆、梁：平行才算）
            //   2. 族群、類型相同（方向不同 → 旋轉錯誤）
            //   3. 任何類型（類型錯誤）
            // 若逐一處理，被移走的牆會先搶走旁邊別的牆（另一種女兒牆、或垂直相接被拉長的同類型牆），
            // 一個錯誤會變成好幾個
            var matchOf = new int[filteredStd.Elements.Count];
            for (int i = 0; i < matchOf.Length; i++) matchOf[i] = -1;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < matchOf.Length; i++)
                {
                    if (matchOf[i] >= 0) continue;
                    matchOf[i] = FindClosest(filteredStd.Elements[i], correctedStudent.Elements, matched, pass);
                    if (matchOf[i] >= 0) matched.Add(matchOf[i]);
                }
            }

            for (int i = 0; i < filteredStd.Elements.Count; i++)
            {
                var stdEl = filteredStd.Elements[i];
                int foundIdx = matchOf[i];

                if (foundIdx == -1)
                {
                    errors.Add(Info(new CompareError
                    {
                        Type = ErrorType.Missing,
                        Category = stdEl.Category,
                        X = stdEl.X,
                        Y = stdEl.Y,
                        Z = stdEl.Z,
                        StudentX = stdEl.X,
                        StudentY = stdEl.Y,
                        StudentZ = stdEl.Z,
                        Level = stdEl.Level,
                        StdElementId = stdEl.Id,
                        RawStudentX = stdEl.X - offset.X,
                        RawStudentY = stdEl.Y - offset.Y,
                        RawStudentZ = stdEl.Z - offset.Z,
                        Detail = $"\u9019\u88e1\u61c9\u8a72\u6709\u4e00\u500b\u3010{stdEl.Category}\u3011({Describe(stdEl)})\uff0c\u4f46\u5b78\u751f\u6a21\u578b\u4e2d\u627e\u4e0d\u5230"
                    }, stdEl));
                    continue;
                }

                var stuEl = correctedStudent.Elements[foundIdx];
                var rawEl = filteredStu.Elements[foundIdx];

                var attrErrors = CheckAttributes(stdEl, stuEl, out var diffKeys);
                if (attrErrors.Count > 0)
                {
                    errors.Add(Info(new CompareError
                    {
                        Type = ErrorType.WrongAttr,
                        Category = stdEl.Category,
                        X = stdEl.X,
                        Y = stdEl.Y,
                        Z = stdEl.Z,
                        StudentX = stuEl.X,
                        StudentY = stuEl.Y,
                        StudentZ = stuEl.Z,
                        Level = stdEl.Level,
                        StdElementId = stdEl.Id,
                        StuElementId = rawEl.Id,
                        RawStudentX = rawEl.X,
                        RawStudentY = rawEl.Y,
                        RawStudentZ = rawEl.Z,
                        Detail = string.Join("\uff1b", attrErrors),
                        DiffKeys = diffKeys
                    }, stdEl));
                }

                if (RotationDiff(stdEl, stuEl) > 1.0)
                {
                    errors.Add(Info(new CompareError
                    {
                        Type = ErrorType.WrongRotation,
                        Category = stdEl.Category,
                        X = stdEl.X,
                        Y = stdEl.Y,
                        Z = stdEl.Z,
                        StudentX = stuEl.X,
                        StudentY = stuEl.Y,
                        StudentZ = stuEl.Z,
                        Level = stdEl.Level,
                        StdElementId = stdEl.Id,
                        StuElementId = rawEl.Id,
                        RawStudentX = rawEl.X,
                        RawStudentY = rawEl.Y,
                        RawStudentZ = rawEl.Z,
                        Detail = $"\u65cb\u8f49\u89d2\u5ea6\u61c9\u70ba {stdEl.Rotation:F1}\u00b0\uff0c\u5b78\u751f\u7684\u662f {stuEl.Rotation:F1}\u00b0"
                    }, stdEl));
                }
            }

            for (int j = 0; j < correctedStudent.Elements.Count; j++)
            {
                if (matched.Contains(j)) continue;
                var stuEl = correctedStudent.Elements[j];
                var rawEl = filteredStu.Elements[j];
                errors.Add(Info(new CompareError
                {
                    Type = ErrorType.Extra,
                    Category = stuEl.Category,
                    X = stuEl.X,
                    Y = stuEl.Y,
                    Z = stuEl.Z,
                    StudentX = stuEl.X,
                    StudentY = stuEl.Y,
                    StudentZ = stuEl.Z,
                    Level = rawEl.Level,
                    StuElementId = rawEl.Id,
                    RawStudentX = rawEl.X,
                    RawStudentY = rawEl.Y,
                    RawStudentZ = rawEl.Z,
                    Detail = $"\u9019\u500b\u3010{stuEl.Category}\u3011({Describe(stuEl)})\u662f\u591a\u9918\u7684\uff0c\u6a19\u6e96\u6a21\u578b\u6c92\u6709"
                }, stuEl));
            }

            // 同一個構件「標準有、學生沒有」＋「附近多一個」：其實是放錯位置
            var missingErrors = errors.FindAll(e => e.Type == ErrorType.Missing);
            var extraErrors = errors.FindAll(e => e.Type == ErrorType.Extra);
            var toRemove = new List<CompareError>();
            var toAdd = new List<CompareError>();

            foreach (var missing in missingErrors)
            {
                // 原本的 6 個類別：同類別、1m 內就配對（同類型的優先）。
                // 其他構件（衛浴、設備、樓梯…）：族群與類型都要相同才配對，範圍放寬到 3m，
                // 避免「少一個馬桶、多一個洗手台」被當成「馬桶放錯位置」
                bool legacy = IsLegacy(KeyOf(missing));
                double radius = legacy ? 1000.0 : 3000.0;

                // 同類型的多餘構件排在前面，優先配對
                var ordered = extraErrors.Where(e => e.FamilyName == missing.FamilyName && e.FamilyType == missing.FamilyType)
                    .Concat(extraErrors.Where(e => !(e.FamilyName == missing.FamilyName && e.FamilyType == missing.FamilyType)));
                foreach (var extra in ordered)
                {

                    if (KeyOf(extra) != KeyOf(missing)) continue;
                    if (toRemove.Contains(extra)) continue;
                    if (!legacy && (extra.FamilyName != missing.FamilyName || extra.FamilyType != missing.FamilyType))
                        continue;

                    double dist = Math.Sqrt(
                        Math.Pow(extra.X - missing.X, 2) +
                        Math.Pow(extra.Y - missing.Y, 2) +
                        Math.Pow(extra.Z - missing.Z, 2));

                    if (dist < radius)
                    {
                        toRemove.Add(missing);
                        toRemove.Add(extra);
                        toAdd.Add(new CompareError
                        {
                            Type = ErrorType.WrongPosition,
                            Category = missing.Category,
                            X = missing.X,
                            Y = missing.Y,
                            Z = missing.Z,
                            StudentX = extra.X,
                            StudentY = extra.Y,
                            StudentZ = extra.Z,
                            Level = missing.Level,
                            StdElementId = missing.StdElementId,
                            StuElementId = extra.StuElementId,
                            RawStudentX = extra.RawStudentX,
                            RawStudentY = extra.RawStudentY,
                            RawStudentZ = extra.RawStudentZ,
                            CategoryKey = missing.CategoryKey,
                            FamilyName = missing.FamilyName,
                            FamilyType = missing.FamilyType,
                            Group = missing.Group,
                            Detail = $"\u3010{missing.Category}\u3011\u4f4d\u7f6e\u504f\u79fb\uff0c\u61c9\u5728 X={missing.X:F0}, Y={missing.Y:F0}\uff0c\u5b78\u751f\u653e\u5728 X={extra.X:F0}, Y={extra.Y:F0}"
                        });
                        break;
                    }
                }
            }

            foreach (var r in toRemove) errors.Remove(r);
            errors.AddRange(toAdd);

            return MergeTypeEdits(MergeGroupMoves(MergeHostedParts(errors)));
        }

        // 屬於「類型」的參數：改的是類型內容，所有用這個類型的構件會一起改變
        private static readonly HashSet<string> TypeLevelKeys = new HashSet<string>
        {
            "\u6700\u5927\u7d1a\u9ad8", "\u6700\u5c0f\u7d1a\u6df1", "\u5e73\u53f0\u539a\u5ea6", "\u6276\u624b\u9ad8\u5ea6", "\u539a\u5ea6"
        };

        // 直接修改了類型內容（例如把「900mm 方管」的扶手高度改成 700），使用這個類型的每個構件都會不同。
        // 學生只做錯一件事，所以類型、差異內容都相同的屬性錯誤合併成 1 個
        private static List<CompareError> MergeTypeEdits(List<CompareError> errors)
        {
            var result = new List<CompareError>(errors);
            var groups = errors
                .Where(e => e.Type == ErrorType.WrongAttr && e.DiffKeys != null && e.DiffKeys.Count > 0 &&
                            e.DiffKeys.All(key => TypeLevelKeys.Contains(key)))
                .GroupBy(e => $"{KeyOf(e)}|{e.FamilyName}|{e.FamilyType}|{e.Detail}")
                .Where(g => g.Count() > 1);
            foreach (var g in groups)
            {
                var members = g.ToList();
                var leader = members[0];
                leader.Related.AddRange(members.Skip(1));
                leader.Detail += $"\uff08\u76f4\u63a5\u4fee\u6539\u4e86\u985e\u578b\u300c{leader.FamilyType}\u300d\u7684\u5167\u5bb9\uff0c\u4f7f\u7528\u6b64\u985e\u578b\u7684 {members.Count} \u500b\u69cb\u4ef6\u90fd\u53d7\u5f71\u97ff\uff0c\u7b97 1 \u500b\u932f\u8aa4\uff09";
                foreach (var e in members.Skip(1)) result.Remove(e);
            }
            return result;
        }


        // 整個構件缺少（或多餘）時，依附在它上面、也一起缺少的構件不另外算錯：
        // 少一座樓梯＝1 個錯誤（不是樓梯＋扶手各一個）；少一面牆＝1 個錯誤（牆上的門窗不另算）。
        // 樓梯還在、只是級深或平台錯，仍然照常各自報錯
        private static List<CompareError> MergeHostedParts(List<CompareError> errors)
        {
            var result = new List<CompareError>(errors);
            foreach (var type in new[] { ErrorType.Missing, ErrorType.Extra })
            {
                var list = errors.Where(e => e.Type == type).ToList();
                Func<CompareError, long> idOf = e => type == ErrorType.Missing ? e.StdElementId : e.StuElementId;
                var byId = new Dictionary<long, CompareError>();
                foreach (var e in list)
                    if (idOf(e) != 0 && !byId.ContainsKey(idOf(e))) byId[idOf(e)] = e;
                var stairs = list.Where(e => KeyOf(e) == "OST_Stairs").ToList();

                foreach (var e in list)
                {
                    CompareError parent = null;
                    if (e.HostId != 0 && byId.TryGetValue(e.HostId, out var p) && p != e)
                        parent = p;
                    else if (e.HostId == 0 && KeyOf(e) == "OST_StairsRailing")
                    {
                        // 有些模型的樓梯扶手沒有記錄依附的樓梯：用位置判斷（3m 內最近的那座）
                        parent = stairs.OrderBy(s => Dist(s, e)).FirstOrDefault(s => Dist(s, e) < 3000.0);
                    }
                    if (parent == null) continue;
                    parent.Related.Add(e);
                    result.Remove(e);
                }

                foreach (var parent in list.Where(e => result.Contains(e) && e.Related.Count > 0))
                {
                    parent.Detail += $"\uff08\u9023\u540c\u4f9d\u9644\u7684 {parent.Related.Count} \u500b\u69cb\u4ef6\uff1a" +
                                     string.Join("\u3001", parent.Related.Select(r => $"{r.Category} {r.FamilyType}")) + "\uff09";
                }
            }
            return result;
        }

        private static double Dist(CompareError a, CompareError b) =>
            Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));

        // 一起被移動的構件算一個錯誤：位移量相同（差 20mm 內）、彼此相鄰（5m 內，可串連）的「位置偏移」合併。
        // 例如一面牆移走時，鎖在一起的另一面牆和牆上的門也跟著移動——學生只做錯了一件事
        private static List<CompareError> MergeGroupMoves(List<CompareError> errors)
        {
            var moves = errors.Where(e => e.Type == ErrorType.WrongPosition).ToList();
            if (moves.Count < 2) return errors;

            int n = moves.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            int Root(int a) { while (parent[a] != a) a = parent[a] = parent[parent[a]]; return a; }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var a = moves[i]; var b = moves[j];
                    double ddx = (a.StudentX - a.X) - (b.StudentX - b.X);
                    double ddy = (a.StudentY - a.Y) - (b.StudentY - b.Y);
                    double ddz = (a.StudentZ - a.Z) - (b.StudentZ - b.Z);
                    if (Math.Sqrt(ddx * ddx + ddy * ddy + ddz * ddz) > 20.0) continue;
                    double dist = Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2) + Math.Pow(a.Z - b.Z, 2));
                    if (dist > 5000.0) continue;
                    parent[Root(i)] = Root(j);
                }
            }

            var result = new List<CompareError>(errors);
            foreach (var grp in Enumerable.Range(0, n).GroupBy(Root).Where(g => g.Count() > 1))
            {
                // 代表的構件：結構 → 牆 → 樓板 → 其他（門窗等跟著移動的放後面）
                var members = grp.Select(i => moves[i]).OrderBy(e => MoveRank(KeyOf(e))).ToList();
                var leader = members[0];
                leader.Related = members.Skip(1).ToList();
                leader.Detail += $"\uff08\u540c\u4e00\u7d44\u4e00\u8d77\u504f\u79fb\u7684\u9084\u6709 {leader.Related.Count} \u500b\u69cb\u4ef6\uff1a" +
                                 string.Join("\u3001", leader.Related.Select(e => $"{e.Category} {e.FamilyType}")) + "\uff09";
                foreach (var e in leader.Related) result.Remove(e);
            }
            return result;
        }

        private static int MoveRank(string key)
        {
            switch (key ?? "")
            {
                case "OST_StructuralColumns": case "\u7d50\u69cb\u67f1": return 0;
                case "OST_StructuralFraming": case "\u7d50\u69cb\u69cb\u67b6": return 1;
                case "OST_Walls": case "\u7246": return 2;
                case "OST_Floors": case "\u6a13\u677f": return 3;
                default: return 4;
            }
        }

        // 錯誤帶上構件的類別代碼、族群、類型、分組
        private static CompareError Info(CompareError err, ElementSnapshot el)
        {
            err.CategoryKey = el.CategoryKey;
            err.FamilyName = el.FamilyName;
            err.FamilyType = el.FamilyType;
            err.Group = GroupOf(KeyOf(el));
            err.HostId = el.HostId;
            return err;
        }

        // 顯示用：「族群 類型」（系統族群或族群名稱跟類型一樣時只顯示類型）
        private static string Describe(ElementSnapshot el)
        {
            if (string.IsNullOrEmpty(el.FamilyName) || el.FamilyName == el.FamilyType) return el.FamilyType;
            return $"{el.FamilyName} {el.FamilyType}";
        }

        // 類別比對用的鍵：有類別代碼用代碼（不受 Revit 介面語言影響），舊的快照沒有代碼就用類別名稱
        private static string KeyOf(ElementSnapshot el) =>
            string.IsNullOrEmpty(el.CategoryKey) ? el.Category : el.CategoryKey;

        private static string KeyOf(CompareError err) =>
            string.IsNullOrEmpty(err.CategoryKey) ? err.Category : err.CategoryKey;

        // 原本就有比對的 6 個類別
        private static readonly HashSet<string> LegacyKeys = new HashSet<string>
        {
            "OST_StructuralColumns", "OST_StructuralFraming", "OST_Walls", "OST_Floors", "OST_Doors", "OST_Windows",
            "\u7d50\u69cb\u67f1", "\u7d50\u69cb\u69cb\u67b6", "\u7246", "\u6a13\u677f", "\u9580", "\u7a97"
        };

        private static bool IsLegacy(string key) => LegacyKeys.Contains(key ?? "");

        // 類別分組（面板的「構件類別」篩選）
        public static string GroupOf(string key)
        {
            switch (key ?? "")
            {
                case "OST_StructuralColumns": case "OST_StructuralFraming": case "OST_StructuralFoundation":
                case "\u7d50\u69cb\u67f1": case "\u7d50\u69cb\u69cb\u67b6":
                    return "\u7d50\u69cb";
                case "OST_Walls": case "OST_Floors": case "OST_Doors": case "OST_Windows": case "OST_Roofs":
                case "OST_Ceilings": case "OST_Ramps": case "OST_ShaftOpening": case "OST_Toposolid":
                case "OST_Topography": case "OST_CurtaSystem": case "OST_Columns":
                case "\u7246": case "\u6a13\u677f": case "\u9580": case "\u7a97":
                    return "\u5efa\u7bc9";
                case "OST_Stairs": case "OST_StairsRailing":
                    return "\u6a13\u68af\u6276\u624b";
                case "OST_PlumbingFixtures": case "OST_Furniture": case "OST_FurnitureSystems":
                case "OST_SpecialityEquipment": case "OST_GenericModel": case "OST_Casework":
                case "OST_LightingFixtures": case "OST_ElectricalFixtures": case "OST_ElectricalEquipment":
                case "OST_MechanicalEquipment": case "OST_Planting": case "OST_Entourage": case "OST_Parking":
                    return "\u8a2d\u5099\u5bb6\u5177";
                case "OST_PipeCurves": case "OST_DuctCurves": case "OST_Conduit": case "OST_CableTray":
                case "OST_FlexPipeCurves": case "OST_FlexDuctCurves":
                    return "\u6a5f\u96fb\u7ba1\u7dda";
                default:
                    return "\u5176\u4ed6";
            }
        }

        // 旋轉角差（0~180°）。線型構件（牆、梁、管線）從哪一端開始畫都一樣，只比到 90°
        private static double RotationDiff(ElementSnapshot a, ElementSnapshot b)
        {
            double d = Math.Abs(a.Rotation - b.Rotation) % 360;
            if (d > 180) d = 360 - d;
            if (a.IsLinear && b.IsLinear && d > 90) d = 180 - d;
            return d;
        }

        private List<ElementSnapshot> FilterByFloor(List<ElementSnapshot> elements,
            string selectedFloor,
            Dictionary<string, (double MinZ, double MaxZ)> floorRanges)
        {
            if (selectedFloor == ALL_FLOORS)
                return new List<ElementSnapshot>(elements);

            return elements.FindAll(el => el.Level == selectedFloor);
        }

        private XYZOffset CalcOffset(ModelSnapshot standard, ModelSnapshot student)
        {
            // 配對投票：拿同類別的每一對構件（標準 × 學生）各算一個候選偏移，
            // 選「套用後能對上最多構件」的那個。不受建模順序影響，
            // 學生少幾根或多幾根構件也不會把偏移拉走（平均座標會被拉偏）。
            // 優先用結構柱投票；沒有柱的樓層（例如屋頂層）改用梁、牆、樓板
            var priority = new List<string>
            {
                "OST_StructuralColumns", "\u7d50\u69cb\u67f1", "OST_StructuralFraming", "\u7d50\u69cb\u69cb\u67b6",
                "OST_Walls", "\u7246", "OST_Floors", "\u6a13\u677f"
            };
            foreach (var el in standard.Elements)
                if (!priority.Contains(KeyOf(el))) priority.Add(KeyOf(el));

            foreach (var key in priority)
            {
                var stdEls = standard.Elements.FindAll(e => KeyOf(e) == key);
                var stuEls = student.Elements.FindAll(e => KeyOf(e) == key);
                if (stdEls.Count == 0 || stuEls.Count == 0) continue;
                return VoteOffset(stdEls, stuEls, GetTolerance(key));
            }
            return new XYZOffset { X = 0, Y = 0, Z = 0 };
        }

        private XYZOffset VoteOffset(List<ElementSnapshot> std, List<ElementSnapshot> stu, double tolerance)
        {
            XYZOffset best = null;
            int bestScore = -1;
            double bestResidual = double.MaxValue;

            // 第一步（快速篩選）：每一對構件的偏移量丟進邊長 = 容許值的格子裡投票，
            // 正確的偏移會讓大部分構件對落在同一格。只挑票數最高的幾格當候選，
            // 不必對每一對都做完整比對（構件上百個時，原本的做法要算好幾分鐘）
            var bins = new Dictionary<(long X, long Y, long Z), Bin>();
            foreach (var s in std)
            {
                foreach (var t in stu)
                {
                    double ox = s.X - t.X, oy = s.Y - t.Y, oz = s.Z - t.Z;
                    var key = (Cell(ox, tolerance), Cell(oy, tolerance), Cell(oz, tolerance));
                    Bin b;
                    if (!bins.TryGetValue(key, out b)) { b = new Bin(); bins[key] = b; }
                    b.Count++; b.SumX += ox; b.SumY += oy; b.SumZ += oz;
                }
            }

            // 每格的票數連同周圍 26 格一起算（避免正確偏移剛好落在格線上被拆票），
            // 候選偏移取這一區所有構件對的平均
            var ranked = new List<((long X, long Y, long Z) Key, int Votes, XYZOffset Offset)>();
            foreach (var key in bins.Keys)
            {
                int votes = 0;
                double sx0 = 0, sy0 = 0, sz0 = 0;
                for (long dx = -1; dx <= 1; dx++)
                    for (long dy = -1; dy <= 1; dy++)
                        for (long dz = -1; dz <= 1; dz++)
                        {
                            Bin nb;
                            if (!bins.TryGetValue((key.X + dx, key.Y + dy, key.Z + dz), out nb)) continue;
                            votes += nb.Count; sx0 += nb.SumX; sy0 += nb.SumY; sz0 += nb.SumZ;
                        }
                ranked.Add((key, votes, new XYZOffset { X = sx0 / votes, Y = sy0 / votes, Z = sz0 / votes }));
            }
            // 票數高的優先；同票時依格子座標排序，確保結果與構件順序無關
            ranked.Sort((a, b) =>
            {
                if (a.Votes != b.Votes) return b.Votes.CompareTo(a.Votes);
                if (a.Key.X != b.Key.X) return a.Key.X.CompareTo(b.Key.X);
                if (a.Key.Y != b.Key.Y) return a.Key.Y.CompareTo(b.Key.Y);
                return a.Key.Z.CompareTo(b.Key.Z);
            });

            // 第二步：只對前幾名候選做完整比對，選能對上最多構件的
            for (int k = 0; k < Math.Min(VOTE_CANDIDATES, ranked.Count); k++)
            {
                {
                    var cand = ranked[k].Offset;
                    int score;
                    double residual;
                    ScoreOffset(std, stu, cand, tolerance, out score, out residual);

                    // 得票相同時取殘差較小者，再相同時取偏移量較小者，確保結果與順序無關
                    bool better = score > bestScore
                        || (score == bestScore && residual < bestResidual - 1e-6)
                        || (score == bestScore && Math.Abs(residual - bestResidual) <= 1e-6 && Length(cand) < Length(best));
                    if (better)
                    {
                        best = cand;
                        bestScore = score;
                        bestResidual = residual;
                    }
                }
            }

            // 微調：用對上的構件的平均差修正偏移，讓結果更精準
            double sx = 0, sy = 0, sz = 0;
            int n = 0;
            foreach (var s in std)
            {
                var t = Nearest(s, stu, best, tolerance);
                if (t == null) continue;
                sx += s.X - t.X; sy += s.Y - t.Y; sz += s.Z - t.Z; n++;
            }
            if (n > 0) return new XYZOffset { X = sx / n, Y = sy / n, Z = sz / n };
            return best;
        }

        private const int VOTE_CANDIDATES = 10;

        private class Bin
        {
            public int Count;
            public double SumX, SumY, SumZ;
        }

        private static long Cell(double v, double size)
        {
            return (long)Math.Floor(v / size);
        }

        // 套用偏移後，有幾個標準構件在容許範圍內找得到學生構件，以及其距離總和
        private void ScoreOffset(List<ElementSnapshot> std, List<ElementSnapshot> stu, XYZOffset d,
            double tolerance, out int score, out double residual)
        {
            score = 0;
            residual = 0;
            foreach (var s in std)
            {
                var t = Nearest(s, stu, d, tolerance);
                if (t == null) continue;
                score++;
                residual += Dist(s, t, d);
            }
        }

        private ElementSnapshot Nearest(ElementSnapshot s, List<ElementSnapshot> stu, XYZOffset d, double tolerance)
        {
            ElementSnapshot best = null;
            double bestDist = double.MaxValue;
            foreach (var t in stu)
            {
                double xy = Math.Sqrt(Math.Pow(t.X + d.X - s.X, 2) + Math.Pow(t.Y + d.Y - s.Y, 2));
                double z = Math.Abs(t.Z + d.Z - s.Z);
                if (xy > tolerance || z > tolerance) continue;
                double dist = Math.Sqrt(xy * xy + z * z);
                if (dist < bestDist) { bestDist = dist; best = t; }
            }
            return best;
        }

        private double Dist(ElementSnapshot s, ElementSnapshot t, XYZOffset d)
        {
            return Math.Sqrt(Math.Pow(t.X + d.X - s.X, 2) + Math.Pow(t.Y + d.Y - s.Y, 2) + Math.Pow(t.Z + d.Z - s.Z, 2));
        }

        private double Length(XYZOffset d)
        {
            if (d == null) return double.MaxValue;
            return Math.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
        }

        private ModelSnapshot ApplyOffset(ModelSnapshot student, XYZOffset offset)
        {
            var corrected = new ModelSnapshot { ModelName = student.ModelName };
            foreach (var el in student.Elements)
            {
                corrected.Elements.Add(new ElementSnapshot
                {
                    Id = el.Id,
                    Category = el.Category,
                    CategoryKey = el.CategoryKey,
                    X = el.X + offset.X,
                    Y = el.Y + offset.Y,
                    Z = el.Z + offset.Z,
                    FamilyName = el.FamilyName,
                    FamilyType = el.FamilyType,
                    Material = el.Material,
                    Width = el.Width,
                    Height = el.Height,
                    Depth = el.Depth,
                    Rotation = el.Rotation,
                    IsLinear = el.IsLinear,
                    Level = el.Level,
                    HostId = el.HostId,

                    Nums = el.Nums,
                    Texts = el.Texts
                });
            }
            return corrected;
        }

        // 位置容許誤差（mm）：超過就算「找不到」（再看附近有沒有同一個構件，判斷是不是放錯位置）
        private static double GetTolerance(string key)
        {
            switch (key ?? "")
            {
                case "OST_StructuralFraming": case "\u7d50\u69cb\u69cb\u67b6":
                    return 100.0;
                case "OST_Floors": case "\u6a13\u677f": case "OST_Walls": case "\u7246":
                case "OST_Roofs": case "OST_Ceilings": case "OST_Toposolid": case "OST_Topography": case "OST_Ramps":
                case "OST_StairsRailing":   // 欄杆很長、用外框中心定位，延伸段重新生成時中心容易移動 200~300mm
                    return 500.0;
                case "OST_Stairs": case "OST_ShaftOpening":
                    return 300.0;   // 樓梯用外框中心定位，改級高或梯段寬度時中心會小幅移動

                case "OST_PlumbingFixtures": case "OST_Furniture": case "OST_FurnitureSystems":
                case "OST_SpecialityEquipment": case "OST_GenericModel": case "OST_Casework":
                case "OST_LightingFixtures": case "OST_ElectricalFixtures": case "OST_ElectricalEquipment":
                case "OST_MechanicalEquipment": case "OST_Planting": case "OST_Entourage": case "OST_Parking":
                case "OST_PipeCurves": case "OST_DuctCurves": case "OST_Conduit": case "OST_CableTray":
                    return 100.0;
                default:
                    return 50.0;    // 柱、門、窗等
            }
        }


        private static bool SameType(ElementSnapshot a, ElementSnapshot b) =>
            (a.FamilyName ?? "") == (b.FamilyName ?? "") && (a.FamilyType ?? "") == (b.FamilyType ?? "");

        // 線型構件（牆、梁、管線）方向相同（平行，容許 5°）；非線型構件不看方向
        private static bool SameDirection(ElementSnapshot a, ElementSnapshot b) =>
            !(a.IsLinear && b.IsLinear) || RotationDiff(a, b) <= 5.0;

        // pass 0：同類型且同方向；pass 1：同類型；pass 2：不限類型
        private int FindClosest(ElementSnapshot target, List<ElementSnapshot> candidates, HashSet<int> used, int pass)
        {
            bool sameTypeOnly = pass <= 1;
            int bestIdx = -1;
            double bestDist = double.MaxValue;
            string key = KeyOf(target);
            double tolerance = GetTolerance(key);

            for (int i = 0; i < candidates.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (KeyOf(candidates[i]) != key) continue;
                if (sameTypeOnly && !SameType(target, candidates[i])) continue;
                if (pass == 0 && !SameDirection(target, candidates[i])) continue;


                var c = candidates[i];
                double xyDist = Math.Sqrt(Math.Pow(c.X - target.X, 2) + Math.Pow(c.Y - target.Y, 2));
                double zDist = Math.Abs(c.Z - target.Z);

                if (xyDist > tolerance) continue;
                if (zDist > tolerance) continue;

                double dist = Math.Sqrt(xyDist * xyDist + zDist * zDist);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            return bestIdx;
        }

        // 比對屬性，回傳每一項差異的說明；keys 是不同的項目名稱（用來判斷是不是「改了類型內容」）
        private List<string> CheckAttributes(ElementSnapshot std, ElementSnapshot stu, out List<string> keys)
        {
            var errors = new List<string>();
            var k = new List<string>();
            keys = k;

            void Diff(string key, string message) { k.Add(key); errors.Add(message); }

            if (!string.IsNullOrEmpty(std.FamilyName) && !string.IsNullOrEmpty(stu.FamilyName) &&
                std.FamilyName != stu.FamilyName)
                Diff("\u65cf\u7fa4", $"\u65cf\u7fa4\u61c9\u70ba\u300c{std.FamilyName}\u300d\uff0c\u5b78\u751f\u7528\u7684\u662f\u300c{stu.FamilyName}\u300d");

            if (!string.IsNullOrEmpty(std.FamilyType) && std.FamilyType != stu.FamilyType)
                Diff("\u985e\u578b", $"\u65cf\u7fa4\u985e\u578b\u61c9\u70ba\u300c{std.FamilyType}\u300d\uff0c\u5b78\u751f\u586b\u7684\u662f\u300c{stu.FamilyType}\u300d");

            if (!string.IsNullOrEmpty(std.Material) && std.Material != stu.Material)
                Diff("\u6750\u8cea", $"\u6750\u8cea\u61c9\u70ba\u300c{std.Material}\u300d\uff0c\u5b78\u751f\u586b\u7684\u662f\u300c{stu.Material}\u300d");

            // 參數（樓梯級高、級深、階數、牆高、扶手高度…）：兩邊都有的才比
            if (std.Nums != null && stu.Nums != null)
            {
                foreach (var kv in std.Nums)
                {
                    if (!stu.Nums.TryGetValue(kv.Key, out double v)) continue;
                    if (Math.Abs(v - kv.Value) > NumTolerance(kv.Key))
                        Diff(kv.Key, $"{kv.Key}\u61c9\u70ba {FormatNum(kv.Key, kv.Value)}\uff0c\u5b78\u751f\u7684\u662f {FormatNum(kv.Key, v)}");
                }
            }
            if (std.Texts != null && stu.Texts != null)
            {
                foreach (var kv in std.Texts)
                {
                    if (!stu.Texts.TryGetValue(kv.Key, out string v)) continue;
                    if (v != kv.Value)
                        Diff(kv.Key, $"{kv.Key}\u61c9\u70ba\u300c{kv.Value}\u300d\uff0c\u5b78\u751f\u7684\u662f\u300c{v}\u300d");
                }
            }

            return errors;
        }

        // 個數類（階數、梯段數、平台數）沒有單位、要完全相同
        private static bool IsCount(string key) => key.EndsWith("\u6578");

        // 參數容許誤差（mm）
        private static double NumTolerance(string key)
        {
            if (IsCount(key)) return 0.5;
            if (key == "\u7d1a\u9ad8" || key == "\u7d1a\u6df1" || key == "\u6700\u5927\u7d1a\u9ad8" || key == "\u6700\u5c0f\u7d1a\u6df1") return 2.0;
            if (key.Contains("\u5bec\u5ea6") || key.Contains("\u539a\u5ea6") || key.Contains("\u504f\u79fb")) return 5.0;
            if (key.Contains("\u9ad8")) return 10.0;   // 牆高、扶手高度
            return 2.0;
        }

        private static string FormatNum(string key, double v)
        {
            if (IsCount(key)) return $"{v:F0}";
            return Math.Abs(v - Math.Round(v)) < 0.05 ? $"{v:F0} mm" : $"{v:F1} mm";
        }
    }
}