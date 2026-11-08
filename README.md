# BIM 作業批改系統

Revit 插件，讓助教自動比對學生模型與標準模型的差異，並在 Revit 中即時標色錯誤位置。

---

## 開發紀錄

### 第一次測試
- 新增 `Class1.cs`
- 功能：讀取柱座標
- <img width="862" height="475" alt="image1" src="https://github.com/user-attachments/assets/95e57fc8-ef3e-4bf1-b05f-d3a301c3fd24" />


### 第二次測試
- 修改 `Class1.cs`
- 安裝 NuGet 套件（Newtonsoft.Json）
- 功能：輸出模型元件參數為 JSON 檔案
- 輸出檔案：`輸出模型元件參數.json`

### 第三次測試
- 新增 `ComparisonEngine.cs`、`CompareCommand.cs`
- 功能：比對標準模型與學生模型
- 步驟：
  1. 開啟兩個模型，各執行 `ExportSnapshot` 產出 JSON
  2. 執行 `CompareCommand`，選擇兩個 JSON 進行比對
- 輸出檔案：`BIM範例模型(標準)_snapshot.json`、`BIM範例模型(學生-少一根柱)_snapshot.json`

### 第四次測試
- 新增 `TaPanel.cs`、`TaPanelCommand.cs`
- 功能：助教側邊欄 UI，顯示學生清單與錯誤詳細
- 步驟：
  1. 準備測試模型，各執行 `ExportSnapshot` 產出 4 個 JSON
  2. 執行 `TaPanelCommand`，載入標準答案與學生答案

### 第五次測試
- 新增 `HighlightManager.cs`、`ExcelExporter.cs`、`HighlightEvent.cs`
- 修改 `TaPanelCommand.cs`、`TaPanel.cs`
- 功能：在模型上三色標色錯誤位置、匯出 CSV 報告
- 輸出檔案：`BIM作業批改報告.csv`

---

## 檔案結構

| 檔案 | 說明 |
|---|---|
| `Class1.cs` | 模型解析器，讀取六類構件並序列化為 JSON |
| `ComparisonEngine.cs` | 空間模糊匹配比對引擎 |
| `CompareCommand.cs` | 比對兩份 JSON 快照的 Revit 指令 |
| `TaPanel.cs` | 助教側邊欄 UI |
| `TaPanelCommand.cs` | 開啟助教面板的 Revit 指令 |
| `HighlightManager.cs` | 在 Revit 模型上執行三色標色 |
| `HighlightEvent.cs` | Revit ExternalEvent 包裝器 |
| `ExcelExporter.cs` | 匯出 CSV 錯誤報告 |
