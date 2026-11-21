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

### 第八次測試

新增3層樓模型測試:

<img width="362" height="662" alt="圖片11111" src="https://github.com/user-attachments/assets/b6d42b9f-b75d-4eff-8a08-9a20fe189051" />

遇到問題:

<img width="382" height="194" alt="圖片2222" src="https://github.com/user-attachments/assets/1904f41d-eaf2-4685-8dca-2b842b631736" />

- 原因： BIM範例模型.rvt 放在同一個資料夾，程式掃到它並開啟，關閉時因為它是當前活動文件而報錯停止。
 
- 解決方法： 關閉時加入判斷，如果是當前活動文件就跳過不關，程式繼續執行其他檔案。

修改:`BatchExportCommand.cs` 

<img width="356" height="522" alt="圖片3333" src="https://github.com/user-attachments/assets/97bb581d-3d73-4acc-8a2d-76f4edb954e1" />


### 第九次測試

- 問題1:學生5少一面牆卻顯示少一個柱子

<img width="201" height="294" alt="圖片1" src="https://github.com/user-attachments/assets/d15b68a6-26bb-48d0-8774-eb20da5ee53d" />  
<img width="400" height="294" alt="圖片2" src="https://github.com/user-attachments/assets/44b02931-7272-46fb-9961-cb7978dc1716" />

`HighlightManager` 在標準模型上找最近的構件時，容差 500mm 太大，找到了附近的柱子而不是牆。
修改`HighlightManager.cs`

<img width="692" height="488" alt="圖片3" src="https://github.com/user-attachments/assets/19053d35-01e8-4c3d-898e-4dc68de048f4" />

- 問題2: 學生7二樓缺柱子，卻顯示在一樓

<img width="209" height="320" alt="圖片4" src="https://github.com/user-attachments/assets/60901c84-2715-44ac-9e35-34a0c23364b1" />  
<img width="401" height="320" alt="圖片5" src="https://github.com/user-attachments/assets/56ebdc38-8b75-4f0b-8230-2f5b9b0c2089" />

結構柱的 Z 座標全部都是 0.0

修改`Class1.cs`、`HighlightManager.cs`

<img width="692" height="454" alt="圖片6" src="https://github.com/user-attachments/assets/7818d244-f1e7-4ad2-8240-0fa7e9a65ead" />

- 新增一位錯一堆的學生，測試正常

<img width="692" height="433" alt="圖片7" src="https://github.com/user-attachments/assets/2681789d-7fb4-4190-811f-d11322361cac" />

- 在 `TaPanel.cs` 裡加一個「清除標色」按鈕

修改: `TaPanel.cs`、`TaPanelCommand.cs`

<img width="370" height="349" alt="圖片8" src="https://github.com/user-attachments/assets/3416c508-7e6b-4a68-ac6b-da972d08b06f" />  
<img width="263" height="353" alt="圖片9" src="https://github.com/user-attachments/assets/55a6cc35-1104-473a-af87-09bcf108dc75" />


### 第十次測試

- 旋轉偵測不到錯誤，因為中心座標相同，所以要加入旋轉判斷 (新增學生17測試旋轉)

<img width="247" height="261" alt="圖片10" src="https://github.com/user-attachments/assets/8e18aef6-2f82-40bf-9c3c-75ffd85b4908" />

修改`Class1.cs`、`ComparisonEngine.cs`、`HighlightManager.cs`

<img width="332" height="259" alt="圖片11" src="https://github.com/user-attachments/assets/78f4c797-52e6-4c96-a9e8-40407fedc6d0" />  
<img width="447" height="325" alt="圖片12" src="https://github.com/user-attachments/assets/e86aa7fe-7f92-4682-a211-4becfae85586" />

- 元件偏移也偵測不到 (新增學生18測試偏移)

<img width="251" height="269" alt="圖片13" src="https://github.com/user-attachments/assets/d08d5445-325f-4409-8e18-56269ce54eb4" />

原本設定偏移量在容差範圍內（300mm），所以系統認為是同一根梁 -> 改成100mm

修改`ComparisonEngine.cs`

<img width="647" height="432" alt="圖片14" src="https://github.com/user-attachments/assets/03eb750d-5be7-44aa-afa8-136c54349066" />

偏移修改後偵測為缺少正確元件、多於偏移元件 -> 難懂

統整進黃色裡面，黃色代表旋轉錯誤和偏移錯誤，之後黃色代表其他錯誤，錯哪裡會在說明裡面

邏輯為系統判斷偵測到「缺少」+「多餘」是同類別且距離近時，偵測為偏移

修改`ComparisonEngine.cs`、`HighlightManager.cs`

<img width="662" height="450" alt="圖片15" src="https://github.com/user-attachments/assets/7e3752ee-4725-42fc-8319-41d6c1d5aa1d" />

目前跑18個3層樓的json需要25秒


### 第十次測試: 大工程

要把學生模型也同步顯示出，使用分割螢幕方式，助教才能清楚檢查

1.	助教在面板選學生
  
3.	插件自動開啟該學生的 .rvt
   
5.	兩個視圖並排（標準模型 + 學生模型）
   
7.	同時在兩邊標色：
   
- 標準模型：標出「這裡應該有但學生沒有」（紅色）

- 學生模型：標出「這裡是錯的地方」（對應顏色）

1.	修改 TaPanel.cs
   
在`StudentResult` 加上 .rvt 路徑

3.	修改 `TaPanelCommand.cs`、`HighlightEvent.cs`
   
點標色時自動開啟學生模型並並排視窗

---------------------------------10 thousand years later… -----------------------------------

<img width="692" height="431" alt="圖片1" src="https://github.com/user-attachments/assets/ae751d1b-08c9-477c-9616-8f3e4cbcf615" />

同時顯示助教和學生端，可以確認系統有沒有判斷錯誤

### 第十一次測試: 

- 問題1: 學生端柱子顯示不對

<img width="437" height="297" alt="圖片2" src="https://github.com/user-attachments/assets/1c3eca20-64d3-4f7b-ba61-c6e77c08b7d6" />

修改`ComparisonEngine.cs`、`HighlightEvent.cs`、`HighlightManager.cs`

<img width="692" height="479" alt="圖片3" src="https://github.com/user-attachments/assets/df7c672e-465c-459c-80a8-907b18caa5c6" />

這樣才對 缺少構件是不會顯示出來的

- 希望在學生缺少的地方補上提示「⬇ 此處缺少構件」

修改`HighlightManager.cs`

<img width="692" height="406" alt="圖片4" src="https://github.com/user-attachments/assets/29a38b18-99e9-443b-a5c9-316edd0ea603" />

### 第十二次測試: 

開下一位學生檔案時，自動關閉上一位學生檔案，並且確保不覆蓋到舊有檔案

捨棄所有修改直接關閉

修改`HighlightEvent.cs`

不會開太多分頁，永遠保持2個(標準模型、學生模型)，畫面乾淨!!!

按清除時，也會將除了標準模型外的其他分頁都關掉

### 第十三次測試:

自動左右分割，容易看

原本需要自己兩個檔案切換或手動切換左右分割視圖

<img width="692" height="363" alt="圖片5" src="https://github.com/user-attachments/assets/2acec4af-cd65-4031-96ed-cdfa85926630" />

修改`HighlightEvent.cs`

好難 找不到並排指令…

找到了，是TileViews

<img width="692" height="411" alt="圖片6" src="https://github.com/user-attachments/assets/05fba6be-7ae3-43d9-9981-5a2f7bb45888" />

把老師和學生視圖換邊，老師在左學生在右

修改`HighlightEvent.cs`

開啟學生模型後，先切換回標準模型視圖，再並排，這樣標準模型會在左邊

<img width="692" height="395" alt="圖片7" src="https://github.com/user-attachments/assets/3a3d1cc6-dfb2-460d-a176-4689899a34fb" />

### 第十四次測試:

- 老師畫面篇掉

<img width="692" height="420" alt="圖片8" src="https://github.com/user-attachments/assets/c3e2e887-611d-4d87-a1b4-c3b5cffbcd72" />

修改`HighlightEvent.cs`

切換回標準模型後，視角重置到 3D 預設視角

<img width="692" height="411" alt="圖片9" src="https://github.com/user-attachments/assets/fb74cac6-8c5a-407b-8f37-cefe62f5b633" />

失敗，但手動調整第一位後，後面就置中了，不管他。

- 	Revit、json選擇的介面不一樣，只能選資料夾，不能選特定檔案

<img width="309" height="302" alt="圖片10" src="https://github.com/user-attachments/assets/08045fc5-e600-4405-8f8a-15fea1a6bd17" />  
<img width="369" height="319" alt="圖片11" src="https://github.com/user-attachments/assets/3e547667-b5e4-47b5-9a4d-c3cd3af22228" />

修改`TaPanel.cs`

<img width="465" height="403" alt="圖片12" src="https://github.com/user-attachments/assets/94d8e766-7996-43b6-b6f4-59184950391e" />

### 第十五次測試:選樓層比對

第一步：`TaPanel.cs` 加樓層下拉選單

第二步：`ComparisonEngine.cs` 加樓層篩選

- 遇到問題: 分析 Z 座標時，把每個不同高度的構件都算成一層，閾值 1500mm 太小了，反而把同一層的不同高度構件分成不同層。

修改方法：直接讀取 Revit 構件的「基準約束」樓層名稱

第一步：在`Class1.cs`  ElementSnapshot 加 Level 欄位

第二步：在 ParseDocument 裡讀取樓層

第三步：`TaPanel.cs` 的 AnalyzeFloors 改成從 Level 欄位分析

第四步：`ComparisonEngine.cs`  FilterByFloor 改成用 Level 欄位篩選

<img width="525" height="578" alt="圖片13" src="https://github.com/user-attachments/assets/2dcdf36b-e7f6-42b9-ab9e-7749e81df538" />

- 問題: 學生2改變梁尺寸沒被分類在樓層錯誤裡面

<img width="412" height="326" alt="圖片14" src="https://github.com/user-attachments/assets/ed4bfb79-cdff-40aa-9e5c-b132faab6f1a" />

梁的 Level 都是空字串 ""，表示讀不到梁的樓層。

樓板：全部是 null

修改`class.cs`，把讀取樓層那段換成多個參數嘗試

<img width="692" height="381" alt="圖片15" src="https://github.com/user-attachments/assets/b6d22616-4d5e-4d8c-a9bf-d231e070584e" />

只顯示選取樓層錯誤


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
