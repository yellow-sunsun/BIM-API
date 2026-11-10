# BIM 作業批改系統

Revit 插件，讓助教自動比對學生模型與標準模型的差異，並在 Revit 中即時標色錯誤位置。

---

## 開發紀錄

### 第一次測試
- 新增 `Class1.cs`
- 功能：讀取柱座標
<img width="862" height="475" alt="image1" src="https://github.com/user-attachments/assets/95e57fc8-ef3e-4bf1-b05f-d3a301c3fd24" />

### 第二次測試
- 修改 `Class1.cs`
- 安裝 NuGet 套件（Newtonsoft.Json）
- 功能：輸出模型元件參數為 JSON 檔案
- 輸出檔案：`輸出模型元件參數.json`
<img width="1917" height="1053" alt="image2" src="https://github.com/user-attachments/assets/38d98c07-4aac-479e-bb4c-ee86bfa9c511" />

### 第三次測試
- 新增 `ComparisonEngine.cs`、`CompareCommand.cs`
- 功能：比對標準模型與學生模型
- 步驟：

  **1. 開啟兩個模型，Add-In Manager → 各執行 `ExportSnapshot` ，產出2個 JSON (檔案名稱: BIM範例模型(標準)_snapshot.json、BIM範例模型(學生-少一根柱)_snapshot.json)**
  
  **2. Add-In Manager → 再執行 `CompareCommand`，選擇兩個 JSON 進行比對**
  
<img width="1917" height="1125" alt="image3" src="https://github.com/user-attachments/assets/46b4caf5-d03f-4e8a-9596-3d96065540f9" />

### 第四次測試
- 新增 `TaPanel.cs`、`TaPanelCommand.cs`
- 功能：助教側邊欄 UI，顯示學生清單與錯誤詳細
- 步驟：

  **1. 準備測試模型（1個標準答案 & 3位學生）**
  
  <img width="295" height="132" alt="image4" src="https://github.com/user-attachments/assets/77356907-7ef8-4e88-8d8e-50f2e803cac8" />

  **2. 依序開啟每個 .rvt 檔案，各跑一次 `ExportSnapshot`，產出 4 個 JSON**
  
  <img width="396" height="136" alt="image5" src="https://github.com/user-attachments/assets/c51d894e-16e6-4e7b-ab9f-5a51e85c4d49" />

  **3. Add-In Manager → 執行 `TaPanelCommand`，載入標準答案與學生答案（JSON）**
  
  <img width="392" height="575" alt="image6" src="https://github.com/user-attachments/assets/8de9f5a7-4a70-49e7-b943-df219f49fd4f" />
  
- 顯示結果:

<img width="464" height="522" alt="image7" src="https://github.com/user-attachments/assets/f497497a-5fa0-4131-a36c-7f89144b9759" />
<img width="464" height="520" alt="image8" src="https://github.com/user-attachments/assets/f4129062-ce5c-4e0f-86e1-2766586895e9" />

### 第五次測試
- 新增 `HighlightManager.cs`、`ExcelExporter.cs`、`HighlightEvent.cs`
- 修改 `TaPanelCommand.cs`、`TaPanel.cs`
- 功能：在模型上三色標色錯誤位置、匯出 CSV 報告
- 在模型上標色
<img width="1917" height="1037" alt="image9" src="https://github.com/user-attachments/assets/dee07e1c-87e8-4359-a7a4-d29e2eeae8fb" />
<img width="1917" height="1080" alt="image10" src="https://github.com/user-attachments/assets/7b1b7eb5-5050-480a-bc13-8732cf2ff8a4" />

- 匯出excel
<img width="1746" height="416" alt="image11" src="https://github.com/user-attachments/assets/9ea8cf13-491e-4e9c-91b1-90cc260f4383" />

- 輸出檔案：`BIM作業批改報告.csv`

### 第六次測試: debug
- 問題 1：按鈕文字顯示「?」

<img width="245" height="289" alt="Image" src="https://github.com/user-attachments/assets/2a1348fc-0ff4-40f8-aa3a-45714066cd09" /> ➡️ 
<img width="268" height="289" alt="Image" src="https://github.com/user-attachments/assets/fa9a5705-096a-4892-99ec-1b023b48d4b0" />

- 問題 2：批次產生所有學生 JSON，不用每個學生都Add-In -> ExportSnapshot

    新增 `BatchExportCommand.cs`

    修改 `Class1.cs`

    Add-In Manager → 執行 `BatchExportCommand`，一次選擇所有學生.rvt檔

<img width="395" height="369" alt="Image" src="https://github.com/user-attachments/assets/97171669-76b9-42ce-acf5-e66ffac693d5" />
<img width="390" height="369" alt="Image" src="https://github.com/user-attachments/assets/de5ea6f7-6db2-4415-88e4-67f32842b60f" />

<img width="665" alt="Image" src="https://github.com/user-attachments/assets/2bf46e35-0694-4fbf-8c5d-b68b58d59e44" />

Add-In Manager → 執行 `TaPanelCommand`

<img width="865" height="455" alt="Image" src="https://github.com/user-attachments/assets/2f5a2aa5-fdc6-407e-a5d5-58cea5bd77a1" />

- 問題 3：切換學生時清除前一位標色

    看學生一檔案後想看學生二，錯誤會疊加

<img width="155" height="149" alt="Image" src="https://github.com/user-attachments/assets/7e7cb58c-b87d-4767-a353-a527f4071f1a" />

修改 `HighlightEvent.cs`

<img width="865" height="552" alt="Image" src="https://github.com/user-attachments/assets/5ad7a630-a2f5-4ec0-8312-b2168b9eec45" />

- 問題 4：學生多餘構建沒有顯示出來，用箭頭指出多餘位置
    修改 `HighlightManager.cs`

<img width="331" height="255" alt="Image" src="https://github.com/user-attachments/assets/ec0be9e7-8a2c-4f2b-a907-84240471de26" />
<img width="458" height="249" alt="Image" src="https://github.com/user-attachments/assets/08d7ed00-0855-4475-b174-7c5507afc5c7" />

學生作業 &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; ➡️ &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; 老師看到

### 第七次測試
- 問題: 少一個板(學生5)沒顯示出來
<img width="273" height="275" alt="Image" src="https://github.com/user-attachments/assets/ec85ef15-c1da-4c93-8a96-24ad46b90cb9" />
<img width="523" height="271" alt="Image" src="https://github.com/user-attachments/assets/6456980f-fe14-4be6-9df9-b30a7a24b58c" />

學生作業 &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; ➡️ &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp; 老師看到

修改 `HighlightManager.cs`、`HighlightEvent.cs`、`Class1.cs`、`ComparisonEngine.cs`

<img width="529" height="375" alt="Image" src="https://github.com/user-attachments/assets/ec6bdc99-620e-4c93-81cc-5179037717d6" />

- 多測試學生6: 位置偏移不應該被偵測出誤差

  修改`ComparisonEngine.cs`
  
  在 ComparisonEngine.cs 的 Compare 方法最上面加偏移校正
  
<img width="311" height="346" alt="Image" src="https://github.com/user-attachments/assets/08f4eb12-bb48-496e-adcf-3fa1fa01478b" /> ➡️
<img width="333" height="344" alt="Image" src="https://github.com/user-attachments/assets/b2b31c91-d7e9-49cf-832b-bd7880c9583e" />

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
