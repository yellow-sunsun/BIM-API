using System.Collections.Generic;

namespace RevitGradingTool
{
    // 模型快照：從 .rvt 讀出的構件資料，比對時只用這些資料（不需要 Revit）。
    // 獨立成一個檔案、不引用 Revit API，測試程式可以直接編譯它。
    public class ElementSnapshot
    {
        public long Id { get; set; }
        public string Category { get; set; }      // 類別名稱（依 Revit 介面語言，例如「衛工裝置」）
        public string CategoryKey { get; set; }   // 類別代碼（例如 OST_PlumbingFixtures），不受介面語言影響，用來分組
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string FamilyName { get; set; }    // 族群名稱（例如「馬桶」）；牆、樓板等系統族群為空
        public string FamilyType { get; set; }    // 類型名稱
        public string Material { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Depth { get; set; }
        public double Rotation { get; set; }
        public bool IsLinear { get; set; }        // 線型構件（牆、梁、管線…）：畫的方向相反不算錯，旋轉角只比到 180°
        public string Level { get; set; }         // 構件所在樓層
        public long HostId { get; set; }          // 依附的主體元件 Id（門窗→牆、樓梯扶手→樓梯）；0 = 沒有依附

        // 需要比對的參數（例如樓梯的級高、級深、階數；牆高；門的翻轉）
        public Dictionary<string, double> Nums { get; set; } = new Dictionary<string, double>();   // 數值（mm 或個數）
        public Dictionary<string, string> Texts { get; set; } = new Dictionary<string, string>();  // 文字（樓層名稱、是/否）
    }

    public class ModelSnapshot
    {
        public string ModelName { get; set; }
        public List<ElementSnapshot> Elements { get; set; } = new List<ElementSnapshot>();
    }
}