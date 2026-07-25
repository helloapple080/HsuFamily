# 家族圖片、淺色主題與跨年度農曆週期設計

日期：2026-07-25  
狀態：已由使用者核准  
適用專案：許氏入口網（Hsu Family Portal）

## 1. 目標

本次升級包含三個可獨立交付、共同遵守低耦合設計的能力：

1. 讓成員、拜拜事件與歷年行程可以上傳、保存及顯示圖片。
2. 提供淺色、深色與跟隨系統三種主題。
3. 將農曆拜拜日期保存為可重複使用的規則，查詢 2026、2036 或其他年份時，都能自動換算成正確國曆日期。

成功標準是更換圖片儲存供應商或農曆換算實作時，不需要改寫成員、事件、行程 Controller 或前端頁面的主要業務流程。

## 2. 交付順序

### 階段一：主題與農曆週期

- 完成三態主題切換。
- 將農曆事件建模為規則，而非預先複製多年事件。
- 在查詢日期範圍時動態展開 occurrence。

### 階段二：媒體核心

- 建立媒體領域資料、Application service、Repository 與 Storage 抽象。
- 完成權限、驗證、補償與測試，不依賴真實 Google 憑證。

### 階段三：Google Drive Adapter 與圖片 UI

- 接入私人 Google Drive 資料夾。
- 完成上傳、預覽、公開性、封面、相簿及顯示。
- 使用真實 Google OAuth 完成部署驗收。

## 3. 架構與職責分離

依賴方向維持 Clean Architecture：

```text
Domain
  ↑
Application
  ├─ IMediaStorage
  ├─ IMediaRepository
  └─ ILunarRecurrenceService
  ↑
Infrastructure
  ├─ GoogleDriveMediaStorage
  ├─ MongoMediaRepository
  └─ LunarCalendarAdapter
  ↑
Api
  └─ HTTP、授權、Rate Limit 與 DI

Frontend
  ├─ components/theme
  ├─ features/media
  ├─ features/calendar
  └─ hooks + API client
```

### 3.1 邊界規則

- Domain 不引用 Google SDK、MongoDB repository 實作或前端農曆套件。
- Application 只認識儲存與農曆介面，不認識 Google Drive File resource。
- Infrastructure 封裝 Google Drive API、MongoDB 與實際農曆換算。
- Api Controller 不直接呼叫 Google SDK 或 MongoDB collection。
- 前端元件不直接呼叫 Google Drive，也不在元件內直接 `fetch`。
- Google Drive File ID、OAuth token 與分享網址不得作為公開 API contract。

## 4. 圖片與媒體設計

### 4.1 MediaAsset

新增獨立 `MediaAsset`，以媒體 ID 關聯成員、拜拜事件或行程：

```text
MediaAsset
├─ Id
├─ StorageProvider: GoogleDrive
├─ StorageObjectId
├─ OriginalFileName
├─ ContentType
├─ SizeBytes
├─ Width
├─ Height
├─ Visibility: Public | FamilyOnly
├─ Purpose: MemberAvatar | Worship | Itinerary
├─ OwnerEntityId
├─ UploadedBy
├─ CreatedAt
└─ DeletedAt
```

`StorageObjectId` 只存在後端資料層。前端與父實體只保存或接收系統自己的 `MediaAsset.Id`。

### 4.2 Google Drive 連線方式

- 使用網站擁有者的 Google OAuth 授權與專用私人資料夾。
- 家族成員只登入許氏入口網，不需要 Google 帳號或 Drive 權限。
- OAuth Client Secret、Refresh Token 與 Folder ID 只放在正式環境 Secret。
- Drive API 一般使用目前不另外收費；容量使用 Google 帳號的 15 GB 免費空間，不足時才升級 Google One。
- `IMediaStorage` 允許未來換成 Google Cloud Storage、Cloudflare R2 或 NAS。

### 4.3 可見性

- `FamilyOnly`：必須登入許氏入口網後才能讀取。
- `Public`：匿名可讀，但仍只能透過系統 Media ID 存取。
- 成員頭像預設 `FamilyOnly`。
- 拜拜與行程照片可逐張切換，預設 `FamilyOnly`。
- 只有 Admin 可以上傳、變更可見性或刪除。
- 公開只代表可讀，不代表匿名可新增、更新或刪除。

### 4.4 限制與驗證

- 第一版接受 JPEG、PNG、WebP。
- 單張上限 10 MB。
- 成員頭像每位一張。
- 每個拜拜事件或行程最多 30 張。
- 前端限制只改善操作體驗；後端必須重新驗證大小、宣告 MIME、檔案簽章與像素尺寸。
- 不接受 SVG、GIF、HTML 或其他可能包含主動內容的格式。
- 儲存名稱由系統產生，不信任使用者檔名。
- 上傳使用獨立 Rate Limit、請求大小限制與稽核事件。

### 4.5 存取流程

```text
瀏覽器 → /api/media/{mediaId}/content → 驗證 Visibility/JWT
       → IMediaService → IMediaStorage → 私人 Google Drive
       ← Content-Type、Cache-Control、nosniff 與圖片內容
```

Google Drive 不設為全資料夾公開。單張媒體是否公開由入口網資料與 API 決定，以避免將所有家庭照片永久暴露。

### 4.6 一致性與刪除

- Drive 上傳成功但 MongoDB 寫入失敗：立即補償刪除 Drive 檔案。
- MongoDB 軟刪除成功但 Drive 刪除失敗：保留待清理狀態並可重試。
- 父實體刪除時，媒體進入軟刪除／待清理流程，不在 Controller 內串接多個基礎設施操作。
- Drive 暫時不可用時，文字資料仍可使用，UI 顯示替代圖與可理解錯誤。

## 5. 農曆跨年度週期

### 5.1 原則

MongoDB 保存一份規則，不為每一年複製事件。查詢日期區間時，由 `ILunarRecurrenceService` 產生該區間內的 occurrence，再與一次性事件合併排序。

```text
CalendarRule
├─ CalendarType: Lunar
├─ LunarMonth
├─ LunarDay
├─ LeapMonthPolicy: RegularOnly | LeapOnly | Both
├─ Frequency: Yearly | Monthly
├─ LunarDays
├─ EffectiveFrom
└─ EffectiveUntil（可空白）
```

### 5.2 支援案例

- 每年農曆八月十五拜拜。
- 每個農曆月初一、十五拜拜。
- 指定只在一般月份、只在閏月，或一般與閏月都發生。
- 可設定生效與結束日期；空白結束日期代表持續有效。

### 5.3 查詢流程

1. API 接收國曆起訖日期。
2. Application service 取得區間內一次性事件與有效週期規則。
3. 農曆服務找出區間可能涵蓋的農曆年份並展開 occurrence。
4. 僅保留落在國曆查詢區間內且符合閏月策略的日期。
5. 與一次性事件合併、依日期與穩定識別碼排序後回傳。
6. occurrence 可以短期快取，但不寫回成大量事件文件。

農曆三十日在小月不存在時，該 occurrence 跳過，不自動移到二十九日。單一規則轉換失敗不應讓整個月份查詢失敗；系統記錄結構化錯誤並略過該 occurrence。

### 5.4 識別與編輯

展開 occurrence 使用可重現識別碼（規則 ID＋國曆日期），不建立新的 MongoDB Entity。編輯或刪除預設作用於整條規則；第一版不提供「只排除某一年」的例外日期，避免過早增加複雜度。

## 6. 主題設計

- 使用既有 `next-themes` 與 CSS variables。
- 模式為 `light`、`dark`、`system`。
- 新使用者預設 `system`。
- 偏好保存在瀏覽器 local storage，不寫入 MongoDB。
- 切換器位於桌面側邊欄使用者區與手機選單。
- Theme control 為獨立元件；功能頁只使用 semantic tokens，不判斷目前主題。
- 避免 hydration mismatch、初次載入閃爍，並保持鍵盤操作與可讀標籤。

## 7. API 概念

- `POST /api/media`：Admin multipart 上傳，指定 purpose、ownerEntityId、visibility。
- `GET /api/media/{id}/content`：依可見性匿名或登入讀取。
- `PATCH /api/media/{id}/visibility`：Admin 變更公開性。
- `DELETE /api/media/{id}`：Admin 軟刪除並排程清理。
- 現有 Member、Event、Itinerary 回應改以媒體摘要或 Media ID 關聯，不回傳 Drive ID。
- 現有事件日期範圍查詢同時回傳一次性事件與週期 occurrence，維持統一 `ApiResponse<T>`。

## 8. 安全要求

- Google 憑證不得進入 Git、前端 bundle、Log 或 API 回應。
- FamilyOnly content 必須驗證入口網 JWT；Admin mutation 必須使用 `AdminOnly` policy。
- 防止 owner ID 猜測、越權關聯及不屬於該實體的圖片操作。
- 圖片回應設定正確 Content-Type、`X-Content-Type-Options: nosniff`、CSP 與相符的 Cache-Control。
- 公開圖片允許共享；UI 必須清楚說明公開後任何取得網址者皆可能查看與轉傳。
- 上傳、變更可見性與刪除保留結構化稽核資訊，但不記錄 token 或圖片內容。

## 9. 測試與驗收

### 9.1 自動測試

- Domain/Application：跨年度、跨農曆年、閏月、大小月、有效區間、媒體權限及補償流程。
- Infrastructure：以 fake storage 驗證 Application 不依賴 Google SDK；Google adapter 使用 mock HTTP 或受控整合測試。
- API：匿名、一般成員、Admin、錯誤 MIME、偽造副檔名、超大檔案、越權 owner、公開與私人讀取。
- Frontend：三態主題、上傳預覽、進度、錯誤、公開性提示、替代圖與相簿操作。
- Architecture guard：禁止 Api 直接依賴 Google adapter；禁止 Domain/Application 引用 Google SDK。

### 9.2 回歸與瀏覽器驗收

- `dotnet test`
- 前端單元測試與 ESLint
- 正式 Next.js production build
- GitHub Pages 靜態 build 與 artifact assertion
- 桌面與手機深／淺模式
- 2036 年農曆拜拜日期正確顯示
- 未登入不能讀私人圖片，登入後可讀；公開圖片可匿名讀
- Drive 失效時文字功能仍正常且 UI 有替代狀態

## 10. 部署與成本

- GitHub Pages 繼續作為公開、虛構、唯讀展示，不連正式 Drive。
- 正式圖片功能需要可執行 .NET API 的部署環境。
- Google 帳號免費容量為 15 GB；Google One 100 GB 目前為每月 US$1.99，且與 Gmail、Google Photos 共用。
- Google Drive API 一般用量目前無額外費用，但 2026 年配額政策已更新，必須監控官方公告與流量。
- 初期採圖片尺寸限制與 WebP/JPEG 壓縮降低容量及後端傳輸成本。

## 11. 資料相容與非目標

- 既有 `AvatarUrl`、`Photos` 欄位保留相容讀取，逐步遷移到 Media ID，不破壞現有 MongoDB 文件。
- 第一版不搬移舊外部 URL 圖片、不做人臉辨識、不做影片、不做公開相簿 SEO。
- 第一版不支援單次 recurrence 例外、不建立多人共同 Google 授權。
- 不將圖片二進位直接存入 MongoDB。

## 12. 完成定義

- 三階段功能均通過自動與瀏覽器測試。
- Google Drive 可替換性由 fake adapter 與 architecture test 證明。
- 2026 與 2036 的固定農曆規則均能得到正確 occurrence。
- 公開／私人存取符合定義，且任何 API 都不洩漏 Drive ID 或憑證。
- 文件、部署環境範例、風險台帳與 Obsidian 副本同步更新。
- PR 所有 CI 全綠後才合併。
