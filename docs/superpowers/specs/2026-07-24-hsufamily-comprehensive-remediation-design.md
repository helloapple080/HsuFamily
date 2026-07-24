# 許氏入口網全面修復設計規格

**日期：** 2026-07-24  
**狀態：** 已取得方向批准，待文件確認後進入實作計畫  
**基準：** `c31f950`  
**依據：** `docs/07-Skills全面自檢報告.md`

## 1. 目標與成功定義

本次工作要修復稽核報告中的 High、Medium 與 Low 項目，同時保留既有功能、MongoDB 使用者資料與 API 相容性。完成後，專案應具備可驗證的安全預設、Clean Architecture 邊界、輸入資源控制、可恢復的前端體驗，以及能在 CI 持續阻擋已知高風險回歸的工程基線。

完成不等同於宣稱正式環境已取得 TLS 憑證、雲端 Secret Manager、異地備份或組織審批。這些外部控制會提供設定範本、fail-fast 檢查與驗收程序，但仍須在實際部署環境完成。

### 驗收結果

- 已知直接依賴 High 弱點歸零，或具有明確、限期且不可利用的例外紀錄。
- Production 無法以不安全 Cookie 或未定義 HTTPS 公開來源啟動。
- API Controller 不直接依賴 Infrastructure 內部實作，業務流程位於 Application Services。
- 所有集合、文字、日期範圍、分頁與農曆欄位有後端上限。
- 一般使用者不會看到管理員寫入操作，後端 `AdminOnly` 仍是最終安全邊界。
- 鍵盤、螢幕閱讀器、觸控尺寸與 reduced-motion 達到專案基線。
- SignalR 能呈現連線狀態，重連後主動重新同步資料。
- 台灣文化內容不再直接採用套件中的中國地區節日清單。
- Backend tests、frontend tests、lint、production build、Playwright 與安全 gates 全部通過。
- 修復台帳、部署手冊、管理簡報與 Obsidian 副本同步更新。

## 2. 不可破壞條件

1. 不執行 `drop database`、清空 collection、刪除 Docker volume 或資料庫重建。
2. 不改變現有 API 路徑；既有 response envelope 與核心欄位保持相容。
3. 新增驗證只影響未來不合法的寫入；既有成員、行程與行事曆資料仍可讀取。
4. Refresh Token 維護只處理認證技術資料，不刪除家族內容；保留所有有效 session，僅限制已撤銷／過期歷史的成長。
5. 不提交真實密碼、JWT Secret、TLS private key 或正式環境連線字串。
6. 每個行為修正先建立可失敗的測試，再修改 production code。

## 3. 採用方案

採用「漸進式、向後相容的全面強化」。不採只修 High 的最小修補，因為無法完成使用者要求；也不採整體重寫，因為會擴大資料與回歸風險。

修復依賴順序如下：

```text
供應鏈與部署安全
        ↓
Application Services 與輸入邊界
        ↓
前端權限、錯誤、即時同步與文化修正
        ↓
自動化測試、安全 gates、文件與簡報
```

實作會拆成可獨立驗收的 Conventional Commits；若某批失敗，不會以未驗證狀態推送。

## 4. 供應鏈與執行環境

### 4.1 前端依賴

- 將 Next.js／`eslint-config-next` 升至已修補且同系列的安全版本。
- 將 Axios 升至 audit 判定的安全版本。
- 使用 lockfile 固定解析結果，執行 lint、unit tests、production build 與瀏覽器回歸。
- CI 執行 production dependency audit；High 以上直接阻擋。

### 4.2 .NET 與容器

- SDK、runtime image 與 Microsoft packages 對齊同一個受支援安全 patch。
- 容器維持非 root；健康檢查與唯讀／最小權限設定不倒退。
- Production 啟用 HSTS，並正確處理可信 reverse proxy 傳來的 scheme。
- 以實際安全版本與映像 digest／SBOM 留下可追溯證據。

### 4.3 CI 安全 gates

- 前端 dependency audit。
- NuGet vulnerable／deprecated package 檢查。
- CodeQL 靜態分析。
- filesystem／container 弱點掃描。
- SPDX 或 CycloneDX SBOM artifact。
- Pull request、main branch 與排程掃描共用相同安全門檻。

所有第三方 GitHub Actions 使用官方來源並固定可審核版本；實作時再次查核 2026 年目前官方用法。

## 5. 安全部署設計

### 5.1 開發與正式環境分離

- 本機開發可使用 HTTP，但必須明確標示為 Development。
- Production compose 不直接對外發布 API port，只允許前端／reverse proxy 內網存取。
- 外部流量由 TLS reverse proxy 統一進入；Cookie 強制 `Secure`。
- Production 缺少 canonical HTTPS origin、JWT secret、Mongo credentials 或將 Secure Cookie 關閉時，API 啟動即失敗。

### 5.2 HTTP 安全策略

- 啟用 HSTS、移除 obsolete `X-XSS-Protection` 與 `X-Powered-By`。
- CSP 改為 nonce／hash 能力，移除 script `unsafe-inline`；若框架限制使 style 仍需 inline，文件化為暫時例外。
- Reverse proxy 加入 request body、header、connection、timeout 與 rate 上限。
- Forwarded Headers 只信任明確的 proxy network，不接受任意來源偽造。

正式 TLS 憑證及 DNS 不會被假造；提供可替換 domain／secret 的部署範本與檢核命令。

## 6. 後端架構與資料流

### 6.1 Application Services

新增以下 Application abstractions 與 services：

- `IAuthService`：登入、refresh rotation、登出與 session 結果。
- `IEventService`：日期查詢、驗證、mapping、CRUD 與通知協調。
- `IMemberService`：成員查詢、寫入、排序及欄位正規化。
- `IItineraryService`：搜尋、日期／參與者查詢、CRUD 與通知協調。
- `ICalendarNotifier`：由 Application 呼叫、Infrastructure 的 SignalR publisher 實作。

Controller 僅負責 HTTP binding、Cookie／CSRF、status mapping 與統一 `ApiResponse<T>`；不得直接引用 `CalendarHub`、Infrastructure settings 或 repository 實作。

### 6.2 驗證與資源控制

建立集中式 validation limits，避免各 DTO 使用不同數字：

- 分頁／搜尋：預設合理值，硬上限 100。
- 行事曆日期查詢：最大 62 天。
- 文字欄位：依用途設定標題、描述、地址、備註、聯絡方式上限。
- 集合：tags、participants、members、activities 與 schedules 皆限制數量及單項長度。
- recurrence interval 必須大於零並有最大值。
- 農曆月份 1–12、日期 1–30；閏月必須能由指定年份驗證時才接受。
- API request body 設置合理大小上限。

Repository 再做防禦性 clamp，避免未經 Controller 的未來呼叫繞過上限。

### 6.3 Refresh Token 與競態

- Rotation 維持原子條件更新及 replay detection。
- 保留所有有效 token，已撤銷／過期 token history 使用原子更新限制最大數量。
- 不記錄 token 原文；既有 token hash 與 family 機制保持相容。
- 第一位管理員初始化改為由 unique index／atomic insert 處理競態，重複建立視為已完成而非啟動失敗。

### 6.4 Rate limiting 與 MongoDB

- Authentication 先建立 user identity，再執行需要 user ID 的一般 API limiter。
- 登入／refresh 保留 IP limiter，並以可信 proxy 後的 client IP 為準。
- MongoDB 查詢加入 limit、穩定排序及必要複合索引；不進行破壞性索引替換。
- 新索引採冪等建立；若正式資料量很大，先在 staging 驗證建立時間。

## 7. 前端體驗與可靠性

### 7.1 權限與錯誤處理

- 使用 auth store 的 `isAdmin` 隱藏管理員 CRUD；API 403 顯示明確權限訊息。
- React Query 對 401、403 與 validation failure 不重試；network／5xx 只有限次退避重試。
- 增加 route `loading.tsx`、`error.tsx` 與具 retry action 的共用 Error State。
- Toast 只作補充，不作唯一錯誤呈現。

### 7.2 Accessibility 與互動

- 加入 skip link、`main` target、具名稱的 navigation 與 `aria-current`。
- 所有互動使用原生 Button／Link 語意；行事曆日期格提供日期 accessible name。
- 可觸控按鈕至少 44×44px，包含前後月、menu、建立與刪除操作。
- 表單補齊 `id`／`htmlFor`、`aria-invalid`、`aria-describedby`、error summary 與正確 autocomplete。
- 支援 `prefers-reduced-motion` 與 Framer reduced-motion；不以 hover 作唯一操作方式。
- 用一致的 Lucide icons 取代裝飾性 emoji，保留必要的文字標籤。
- 以可存取確認 Dialog 取代瀏覽器 `confirm()`。

### 7.3 SignalR

- Hook 回傳 `connecting`、`connected`、`reconnecting`、`disconnected` 狀態。
- UI 顯示非侵入式連線狀態。
- `onreconnected` 後 invalidates calendar／itinerary queries，補回離線期間事件。
- cleanup 不更新 unmounted component；重連失敗提供手動重新整理／重試。

### 7.4 台灣農曆與節日

- 不直接顯示 `solar.getFestivals()` 的地區性清單。
- 由本專案維護台灣適用的國曆節日 allowlist，農曆節日使用明確 month/day mapping。
- 清明、冬至等節氣由節氣資料處理，避免與地區節日混用。
- 新增農曆生日立即產生合法 `displayText`；後端會重新正規化，避免信任前端字串。
- 補閏月、正月、初一、邊界值與「7 月 1 日不得顯示建党节」測試。

## 8. 測試策略

所有行為修改遵循 red → green → refactor：

1. 先新增能重現缺口的失敗測試。
2. 以最小 production change 通過。
3. 再整理重複或抽象，不改變已通過行為。

### Backend

- Application service unit tests：權限外的業務流程、notifier 呼叫、not-found、validation。
- 認證測試：rotation、replay、history bound、Cookie／CSRF 與 production fail-fast。
- API integration tests：公開／登入／Admin、日期與 limit 邊界、錯誤 envelope。
- Architecture tests：Domain／Application 不得依賴禁止的 projects，Controller 不得引用 Infrastructure namespace。

### Frontend

- Pure function tests：retry policy、農曆／節日 formatting、validation schema。
- Component tests：admin controls、表單關聯、error retry、SignalR status。
- Playwright：登入、授權頁、CRUD mock flows、375／768／1024／1440 viewport、鍵盤與 reduced-motion。
- 自動 accessibility scan；重大違規阻擋 CI。

若本機缺少 .NET／Docker，相關驗證由 GitHub Actions 執行並以 run URL／log 作證據，不以「應該會通過」取代結果。

## 9. 文件與營運證據

- `07-Skills全面自檢報告.md`：每個 finding 標記修復狀態與 commit／test 證據。
- 安全風險台帳：保留原風險、修正內容、殘餘風險與責任邊界。
- 部署手冊：TLS、secret、backup／restore drill、monitoring、rollback 與 release checklist。
- 管理簡報：用非技術語言呈現「已由程式控制」與「仍需組織／正式環境完成」的差異。
- Obsidian：同步 Markdown 與簡報連結，不取代 Git 內的 source of truth。

## 10. 回復與發布策略

- 每個修復批次使用獨立 commit，方便 revert。
- 不在測試失敗時推送宣稱完成的 commit。
- Dependency／configuration 更新先於架構大改，以便快速消除公開 High 風險。
- Database 只有非破壞性的 index／bounded history 行為；若執行正式部署，先做備份與 restore drill。
- 最後以 GitHub CI、dependency scan 與 Playwright 全綠作為推送／合併門檻。

## 11. 明確殘餘責任

程式庫可以提供安全預設與自動檢查，但以下項目必須由實際環境／組織完成後才能宣稱 production-ready：

- 正式網域、TLS 憑證與邊界 WAF／reverse proxy。
- Secret Manager、定期輪替與 break-glass 程序。
- MongoDB 備份、加密、restore drill 與資料保留政策。
- 監控告警、事故演練、權限審批與 code-owner 規則。
- 正式資料量下的效能、容量、RTO／RPO 與災難復原驗證。

因此最終結論將區分「repository 已完成的企業級工程控制」與「外部環境仍待驗收的營運控制」，不做超出證據的宣稱。
