---
title: 許氏入口網 Cloudflare 免費方案設計
tags: [HsuFamily, Cloudflare, architecture]
updated: 2026-11-16
code_baseline: 93d2016
---

# 許氏入口網 Cloudflare 免費方案設計

## 決策與範圍

私人入口網改以 **React＋Vite 靜態網頁、同源 Cloudflare Worker API、Access、D1 與私人 R2** 為目標。使用 Cloudflare 核發的 HTTPS `workers.dev` 網址，不買網域，不需要家中主機常駐。Workers、Access、D1、R2 Standard、Images 都先依免費額度設計；這是費用目標，不是帳單保證。Access 和 R2 開通需要付款資料，R2 超額可計費。本階段不開通雲端服務、不部署、不遷移正式資料。

保留現有 React 介面、響應式和深淺主題、React Query 請求、國農曆行事曆、跨年拜拜規則、成員、歷年行程及私人照片。保持 `lunar-javascript` 與原有的閏月、缺日、民用日期語意。移除對部署層的 Next.js 伺服器綁定，重用現有可移植的 UI、領域規則及資料契約，並維持 `/api/hosted` 路徑與 `ApiResponse<T>`（照片二進位除外）；登入改由 Access 處理，舊的密碼登入 API 不沿用。GitHub Pages 仍是獨立唯讀假資料展示，不含私人 API、儲存端點或正式資料。

不採用只換到 Workers、後端仍留 Supabase 的混合方案，因為這不會解決原本的閒置暫停。也不為家用網站先加 WebSocket、自建登入、代理服務或家中主機。現有 Supabase 版本和既有 MongoDB、照片皆保留；Cloudflare 開發期與既有路徑隔離，不造成原站切換。

## 職責分離

| 邊界 | 職責 |
|---|---|
| React／Vite | 僅交付網頁殼與公開靜態資源，不嵌入私人資料或祕密。頁面沿用功能元件、表單和 hooks；透過 React Query 讀取 API，儲存後及回到可見頁面時重新查詢，照片按需下載。 |
| Worker HTTP 層 | 路由、Access 憑證驗證、家人資格與角色、請求來源／內容／大小檢查、統一錯誤和回應。API 路徑必須先進 Worker，不能落入 SPA 的靜態回退。 |
| 領域層 | 驗證日期、拜拜規則、活動、成員、行程及照片關聯；不直接依賴 Cloudflare SDK。資料表與日期規則錯誤回傳可理解的訊息，不揭露內部錯誤。 |
| 資料與照片層 | D1（SQLite）存紀錄、資格、角色及照片索引，以帶參數 SQL 存取；R2 只存驗證後的圖片本體。外部瀏覽器不拿 D1 或 R2 憑證。 |

多人同步先用共用 D1：儲存後更新當前畫面，回到頁面時刷新過期查詢，不加輪詢或推播。加入更新版本檢查：兩個管理員打開同一筆資料，舊版本寫入回應 `409`，前端保留未儲存內容並提示更新，避免無意覆蓋。

## 登入、權限及隱私

- Access 以核准信箱白名單和一次性登入碼保護正式 `workers.dev`；測試環境用不同應用、audience、D1 和 R2。沒有使用需求的 preview/version URL 關閉；保留的測試 URL 要有獨立的 Access 保護。
- **同時啟用 Workers Static Assets 時，內部路由不會轉傳 `ctx.access`。** API 驗證 `Cf-Access-Jwt-Assertion` 的簽章、信任的發行者、環境專用 audience、時效與使用者身分。只從固定的 Access 團隊端點取得驗簽公鑰，不信任來自瀏覽器的電子郵件、角色或未驗簽宣告。拒絕服務令牌作為家人身分；本機測試以假金鑰與假身分進行，不把繞過模式帶進可部署版本。
- 通過上述驗證後，每次私人 API 操作皆在 D1 檢查仍啟用的資格並取得角色。初版由站主透過受限的雲端管理流程建立首位管理員、核准信箱、分配角色或撤權，不提供公開註冊或角色管理 API。首次啟用以原子寫入將預先核准的信箱綁定已驗證的 Access 使用者識別；識別變更必須重新核准。家人只能查詢，管理員才可修改；不提供自行提權。撤權以應用程式停用資格為準，後續新請求即使帶有效 Access session 也不能讀寫或取圖。
- D1 沒有原 PostgreSQL RLS 這層保障。新版以伺服器專用 binding 加預設拒絕的路由授權取代，不能把舊 SQL 權限當作已搬遷。拒絕跨來源寫入、錯誤內容類型及過大請求；私人資料與圖片一律 `Cache-Control: private, no-store`。日誌不含私人紀錄、原始照片或憑證。
- 沿用限制腳本來源、禁止框架嵌入等瀏覽器安全基線；由 Vite 改為靜態網頁後，驗證 CSP 對正式建置有效，不以開放不受限的 inline script 或 `unsafe-eval` 遷就建置。

## 私人照片

上傳僅限管理員；Worker 檢查檔案宣告與實際格式、大小、解碼與圖片尺寸。先以 Images binding 將私人位元資料轉成 WebP，不公開原圖；延續現有最長邊 1,280 像素、輸入 2 MiB、儲存 1 MiB 的上限。確認輸出已去除 EXIF（含 GPS）後再存入私人 R2。沿用現有每筆紀錄的照片數量及槽位限制，不把瀏覽器端壓圖當作安全措施。

禁止公開 `r2.dev` 或公開 bucket。讀圖只走同源 API；先驗帳號與紀錄關聯，再讀取 R2，不發放可轉傳的公開位置。D1 與 R2 無跨服務交易：資料索引和檔案寫入任一步失敗要可補償及重試；孤兒物件不得被使用者存取，清理由維運手冊記載。免費層下必須用有效的大尺寸圖片、假圖片及上限附近的請求測試；若超過運算／轉檔額度，拒絕上傳並顯示可重試的訊息，不退回未驗證的原圖，也不自動升級。

## 免費額度與成本控制

| 服務 | 本設計的主要額度與取捨 |
|---|---|
| [Workers Free](https://developers.cloudflare.com/workers/platform/pricing/) | 10 萬次動態請求／天、每次 10 ms CPU。靜態資源若不喚起 Worker，不計動態額度。正式候選需測量 API 與圖片上傳最重路徑的 CPU 使用。 |
| [Access Free](https://www.cloudflare.com/plans/) | 最多 50 位使用者。開通需填付款資料；入口網仍保有自己的資格與角色。 |
| [D1 Free](https://developers.cloudflare.com/d1/platform/pricing/) | [單一資料庫 500 MB](https://developers.cloudflare.com/d1/platform/limits/)、帳戶總 5 GB；每天 500 萬列讀、10 萬列寫。索引、分頁及限制查詢範圍，避免全表重掃。 |
| [R2 Standard](https://developers.cloudflare.com/r2/pricing/) | 10 GB-month、每月 100 萬次 A 類及 1,000 萬次 B 類操作；R2 傳出網際網路不收流量費。要開通計費，超額可收費；每週檢查容量與讀寫，接近 70% 提前評估，這不是硬性扣款上限。 |
| [Images Free](https://developers.cloudflare.com/images/pricing/) | 每月 5,000 次不同的轉檔；只在上傳時轉一次，瀏覽不重轉。不使用付費的 Images 圖床。 |

## 分段交付與驗收

1. **本機資料與角色。** 在既有版本旁獨立開發 Worker 和 D1；先用假資料驗 API：無效或過期憑證、錯誤 audience、假身分、唯讀寫入、撤權後請求都被拒絕，管理員及允許的查詢成功；驗證跨來源寫入、農曆 2026／2036、閏月、缺日及版本衝突。用 SQLite 版遷移與測試，不將 PostgreSQL 匯入 SQL 直接用在 D1。
2. **介面與私人照片。** 接回現行 React 功能和主題，接入本機 D1／R2 測試資料；驗證合法圖片、假圖片、過大檔案、去除 EXIF、照片槽位、同源下載、撤權與失敗補償。公開展示包重新檢查只含假資料。
3. **隔離雲端驗收。** 日後取得環境存取權才用假資料建立 Cloudflare 測試環境，核對 Access 登入、靜態資源和 API、角色、跨瀏覽器、實際免費層 CPU／轉檔、圖片隱私和備份還原。開發者和家人手冊同步更新 Markdown 與 HTML，標出哪些項目已在雲端驗過。

## 備份、遷移與回退

來源資料先做可還原的 MongoDB 備份及照片備份，再從唯讀匯出轉為 D1 所需格式；以假資料演練，核對筆數、關聯、時區與農曆規則。帳號不搬密碼或權杖，按核准信箱啟用；不把舊公開圖片網址當成新版照片。舊 MongoDB 離線工具及備份依然保留。

日常備份分開輸出 D1 資料及 R2 私人照片檔，建立檔案與資料關聯清單、校驗雜湊，再加密存到不同位置。上線前先從備份在隔離環境實際還原並核對。D1 Time Travel 可輔助資料庫復原，但不能取代 R2 照片備份。正式切換要有比對紀錄；相同真實資料只允許一邊寫入。若要回退，先停止新站寫入、保留新站變更並對帳，再恢復原站，不刪舊資料庫或照片。

官方操作參考：[React＋Vite](https://developers.cloudflare.com/workers/framework-guides/web-apps/react/) · [Workers Access 限制](https://developers.cloudflare.com/workers/configuration/cloudflare-access/#ctxaccess-limitations) · [Access JWT 驗證](https://developers.cloudflare.com/cloudflare-one/access-controls/applications/http-apps/authorization-cookie/validating-json/) · [私人圖片轉檔](https://developers.cloudflare.com/images/optimization/binding/) · [D1 匯出與匯入](https://developers.cloudflare.com/d1/best-practices/import-export-data/)。
