# 許氏入口網開發與部署手冊

[切換至使用者手冊](user.md) · [CI/CD 照做手冊](ci-cd.md) · [瀏覽器版](developer.html)

## 私人入口網：Cloudflare／React＋Vite

新入口網沿用 React 介面，部署到 `workers.dev` 可不買網域、不架家用主機。`frontend/spa` 組裝 React，`frontend/src/components/features`、`hooks` 和領域契約供兩個入口共享；`frontend/edge` 集中驗證 Access、同源及角色後，再存取 D1 和私人 R2。Cloudflare 沒有 PostgreSQL RLS：D1 只透過 Worker binding 存取，不對外公開。讀取與重新開啟頁面用共用資料；管理員編輯或刪除時用版本比對，防止兩人互蓋。這不是即時推播。

要送出改動、看 GitHub Actions、發布展示版或更新隔離站，請照 [CI/CD 照做手冊](ci-cd.md)：工作分支是自動檢查，公開展示由 `main` 發布，私人站在 CI 綠燈後由維運者手動部署。首次建立環境才使用下面的設定步驟，日常更新不需要重新建庫或放寬白名單。

### 本機測試（不登入、不部署）

```sh
cd frontend
pnpm install --frozen-lockfile --ignore-scripts
pnpm lint
pnpm test
pnpm check:cloudflare
pnpm exec wrangler d1 migrations apply DB --local
pnpm dev:cloudflare
```

`check:cloudflare` 只建置 Vite 和檢查 Worker 打包。開啟 Wrangler 顯示的本機網址可查看網頁殼；私人 API 無驗簽設定回 503。沒有可部署的測試身分／驗簽後門。登入和照片的真實服務行為還須雲端驗證；本機測試不構成上線許可。

### 響應式版面與回歸驗收

- `frontend/spa/index.html` 設定手機視窗；共用 `PortalLayout` 以 `md` 斷點切換手機上方選單和桌面側欄。活動月曆保留七欄、只允許月曆區左右捲，不應讓整頁橫向捲動。
- 在隔離站使用已核准帳號，以 320×568、390×844 的 CSS 視窗尺寸檢查主要頁面與選單，實際換月、搜尋並開啟表單；比較整頁捲動寬度與視窗內容寬度。Safari／Chrome 真機另外測斷點、觸控、地址列、鍵盤遮擋及回到上一頁。
- 行程表單回歸：把視窗切到 568×320，選「歷年行程 → 新增行程」。表單外框須留在視窗內，表單應可獨立上下捲到取消與儲存。空表單按「儲存」只顯示必填錯誤，不新增資料；再按「取消」應可關閉。舊版外框約 `-97` 到 `417` px，不可捲；修正版約 `16` 到 `304` px，可捲。修正在 `features/itineraries/ItineraryDialog`，未改共用 shadcn/ui 元件、API 或角色。

### 目前進度與零額外扣款規則

先看[這次實作紀錄與可重做步驟](cloudflare-staging.md)。Workers Free 與測試 D1 已有一個受 Zero Trust Free 保護的固定網址；只允許一個 Cloudflare 帳號，已驗證匿名進不去、管理員可讀取假資料。R2 已開通，但本入口網沒有建立桶，也沒有連接 R2 或 Images。下列完整驗收流程含**未獲執行許可的超額計費服務**，不是已完成的清單。零額外扣款是硬限制時，請勿執行其中 R2／Images 連接步驟。

### 完整雲端驗收流程（目前只執行免費且已驗證的部分）

1. 維運者先在 `frontend` 執行 `pnpm exec wrangler login` 完成瀏覽器授權，再用 `pnpm exec wrangler whoami` 核對帳戶。開啟 Cloudflare Zero Trust Free 與 R2 Standard，若介面要求升級或有額外費用先停。建立**獨立**測試 Worker、D1 資料庫及 R2 桶，命名例如 `hsu-family-staging`。桶的 Public Development URL（`r2.dev`）、公開網域通通關閉。正式和測試不得共用 D1、R2 或 Access audience。
2. 在 `frontend` 複製 `wrangler.jsonc` 為受 Git 忽略的 `wrangler.staging.local.jsonc`。換成測試 Worker 名稱、D1 名稱、DB 網頁顯示的 UUID 與 R2 名稱；保留 `preview_urls: false`、`run_worker_first` 及 Images binding。第一次可用 `workers_dev: false` 和不設 `vars` 建立 Worker，不公開路由，API 仍拒絕。先執行本機驗證，再分別執行 `pnpm exec wrangler d1 migrations apply DB --remote --config wrangler.staging.local.jsonc`、`pnpm exec wrangler deploy --config wrangler.staging.local.jsonc`。核對終端目標是測試用資源；沒有 Access 前不放資料、不提供網址。
3. 在測試 Worker 的 Access 頁選 `All traffic`，逐一核准測試信箱，不開 `Everyone`、`Bypass` 或服務權杖。目前測試站使用 Cloudflare 帳號；未來家人可評估使用 [Email one-time PIN](https://developers.cloudflare.com/cloudflare-one/integrations/identity-providers/one-time-pin/)，變更登入方法後要重新驗證身分綁定。Access 政策先套用，再在**測試設定檔**啟用 `workers_dev: true`、保留 `preview_urls: false`，先部署不含 `vars` 的設定。確認無痕瀏覽需要登入後，加入以下 `vars`，以實際應用替換示範值，重新部署並再次確認未登入無法存取。

```json
"vars": {
  "PORTAL_ORIGIN": "https://hsu-family-staging.example.workers.dev",
  "ACCESS_ISSUER": "https://example-team.cloudflareaccess.com",
  "ACCESS_AUDIENCE": "在 Access 應用程式裡複製的 64 位十六進位 AUD"
}
```

4. 在那個**測試 D1** 的 SQL Console 執行以下示意。UUID 使用 `crypto.randomUUID()` 產生，信箱改成兩個有權收登入碼的測試信箱、全小寫；Access 白名單也要含這兩個信箱。第一次成功存取會綁定 Access 身分。撤權把 `portal_access.active` 設為 `0` 並將信箱移出 Access 白名單；不要刪除有歷史紀錄的帳號。角色只由雲端維運者設定。

```sql
INSERT INTO portal_access (id, email, role)
VALUES ('<管理員 UUID>', 'admin@example.com', 'Admin'),
       ('<唯讀家人 UUID>', 'reader@example.com', 'User');
```

5. 使用兩個瀏覽器帳號及無痕模式驗收：無登入／未白名單、管理員、唯讀、撤權後開啟既有照片連結、同時修改舊版本、2026／2036 及閏月、手機與深淺主題。測合法 WebP、帶 EXIF/GPS 的 JPEG、假圖片、接近 2 MiB、讀取不快取、重新整理 `/members` 和 API `404` 不變網頁。檢查 Cloudflare Worker 指標中最重請求的 CPU 時間與 Images 轉檔是否可在該帳戶免費層工作；未通過不升級也不正式切換。

### 費用、備份與回復

- [Workers Free](https://developers.cloudflare.com/workers/platform/limits/) 10 萬動態請求／天、單次 CPU 10 ms；[Access](https://www.cloudflare.com/plans/zero-trust-services/) 免費最多 50 人；[D1](https://developers.cloudflare.com/d1/platform/pricing/) 每天 500 萬列讀、10 萬列寫、帳戶總 5 GB（[單一資料庫 500 MB](https://developers.cloudflare.com/d1/platform/limits/)）。同時使用受上述請求與資料庫額度限制，沒有另外的同步服務費。
- [R2 Standard](https://developers.cloudflare.com/r2/pricing/) 每月 10 GB-month、100 萬次 A 類、1,000 萬次 B 類操作免費，超額可收費。這不是零費用硬上限；使用 70% 前就檢查，涵蓋同帳戶其他用途與測試環境。[Images](https://developers.cloudflare.com/images/pricing/) 文件列出免費每月 5,000 個不同轉檔，舊版 [R2 上傳教學](https://developers.cloudflare.com/images/tutorials/optimize-user-uploaded-image/) 對 binding 仍要求 Paid，必須在實際 Free 帳戶驗證；不能轉檔就拒絕上傳，不存原圖、不自動升級。
- [D1 匯出](https://developers.cloudflare.com/d1/best-practices/import-export-data/) 可使用 `pnpm exec wrangler d1 export DB --remote --output <備份檔路徑> --config wrangler.staging.local.jsonc`；照片要用 R2 的私有 S3 工具獨立複製、做 SHA-256 清單，不能把 SQL 當成照片備份。停寫後取得一致備份，放在不同位置並加密。從空的隔離 D1／R2 演練還原、核對每筆圖片索引及內容再切換。D1／R2 無跨服務交易；部分失敗後先用管理介面重試，仍未恢復時停寫、比對私有索引與 R2 清單，備份並人工清理，勿直接刪可見圖片。
- 既有 PostgreSQL SQL 和 MongoDB 離線匯出不能直接套用 D1。搬移前先輸出唯讀備份並核對紀錄、時區、農曆與照片。此分支不會搬既有資料或登入密碼；正式搬移和實際還原需要單獨演練。只讓一個正式站接受寫入，回退先停止新站寫入並保留新站變更做對帳。

[Cloudflare 官方 React／Vite](https://developers.cloudflare.com/workers/framework-guides/web-apps/react/) · [Workers Access 設定](https://developers.cloudflare.com/workers/configuration/cloudflare-access/) · [Wrangler 設定](https://developers.cloudflare.com/workers/wrangler/configuration/)

## 保留的回退版本：Next.js／Supabase

既有私人入口網包含家人登入、農曆拜拜規則、活動月曆、成員、行程搜尋與私人照片。回退只需原有 Next.js 應用程式及 Supabase。更早的執行程式已移出工作分支，可由 Git 提交 `5e15500` 取回；這不影響既有部署或資料。在隔離雲端驗收、資料核對及還原演練前不可切換正式站。

私人家用可先用 Vercel Hobby 搭配 Supabase Free，不需買網域。Supabase Free 含 500 MB 資料庫與 1 GB 檔案空間，低活動約一週可能暫停。照片、流量與備份仍需管理，免費層不是可用性保證。[Vercel 方案](https://vercel.com/docs/plans) · [Supabase 費率](https://supabase.com/pricing)

### 啟動隔離預覽

1. 建立獨立的 Supabase 測試專案，只使用虛構資料。在確認專案 ID 後，使用 Supabase CLI 連到該測試專案，先執行 `supabase db push --dry-run --remote` 核對遷移，再執行 `supabase db push --remote`。目前由 `202611110001_portal_foundation.sql` 至 `202611120003_calendar_events.sql` 共四個遷移。不要混用手動 SQL 及 CLI 重複套用同一批遷移。照片政策需要 Supabase Storage 提供 `storage.allow_any_operation`。
2. 關閉公開註冊，透過 Supabase Dashboard 的 Authentication 建立兩個測試帳號，確認信箱狀態。用各自的 Auth UUID 在 SQL Editor 授權（下例 UUID 必須換成該專案的測試帳號 UUID）：

```sql
insert into public.portal_access (user_id, role) values
  ('<測試管理員 UUID>', 'Admin'),
  ('<測試家人 UUID>', 'User');
```

3. 本機將 `frontend/.env.example` 複製為 `frontend/.env.local`，填入下列變數後執行 `pnpm dev`。Vercel 預覽用獨立專案，Root Directory 設為 `frontend`，使用固定 HTTPS 網址作為 `PORTAL_ORIGIN`（不加結尾斜線，不使用每次部署都不同的網址）。Supabase 只用 Publishable key，不用 service-role 或 secret key。

```dotenv
PORTAL_ORIGIN=http://localhost:3000
SUPABASE_URL=https://YOUR-TEST-PROJECT.supabase.co
SUPABASE_PUBLISHABLE_KEY=sb_publishable_REPLACE_ME
PORTAL_PHOTO_UPLOAD_KEY=<同一測試專案的 64 位十六進位隨機值>
```

4. 用密碼管理工具或 `openssl rand -hex 32` 為每個環境產生獨立的上傳密鑰。把同一值存為伺服器環境變數和隔離資料庫的 `portal_private.storage_upload_key` 單筆資料；不要加 `NEXT_PUBLIC_` 前綴，也不要寫入 Git、聊天、共用手冊或日誌。下列 SQL 的參數須由管理者透過私密的資料庫操作注入，不要在共用編輯器儲存含真實值的查詢：

```sql
insert into portal_private.storage_upload_key (singleton, secret)
values (true, decode('<私密上傳密鑰>', 'hex'))
on conflict (singleton) do update set secret = excluded.secret;
```

5. 以管理員建立及編輯拜拜規則、月曆活動、成員與行程，切換 2026／2036，查詢行程，為每種可掛照片的紀錄上傳並讀取測試圖片。檢查假圖片、超過 2 MiB、重複槽位、帶照片刪除紀錄都被拒絕。一般家人只能讀，不能產生 Storage 簽名網址；未登入或撤權後立即不能取得圖片。跨站寫入應回 403。HTTPS Cookie 應有 HttpOnly、Secure、SameSite=Strict，私人 API 應有 `Cache-Control: private, no-store`。

登入與資料使用同源 `/api/hosted`；API 和 RLS 各自檢查角色。照片走私人桶，伺服器驗證實際格式、縮圖成 WebP 並去除 EXIF 後，才發出限帳號、檔名與期限的上傳憑證。Storage 不與應用資料共用交易；若並行刪除造成殘留物件，普通家人仍讀不到，應在測試專案驗證補償與管理員清理流程。不得用 SQL 刪除 Storage 物件。公開展示版不包含私人 API；私人 API 預設啟用，環境設定缺少時回傳 503，不會回退到舊站。[Storage 權限](https://supabase.com/docs/guides/storage/security/access-control)

在 `frontend` 執行 `pnpm test`、`pnpm lint`、`pnpm audit:prod`、`pnpm test:csp`、`pnpm build`，另執行 `pnpm build:pages` 和 `pnpm test:pages`。RLS 測試使用嵌入式 PostgreSQL；登入和照片測試用真實 SDK 搭配模擬服務。這些測試不能取代託管 Auth 與 Storage 的驗收。

### 搬遷、備份與回復

先用[離線搬遷與還原手冊](../superpowers/notes/2026-11-12-legacy-converter.md)轉換假資料，核對成員、行程、月曆、關聯及筆數。工具不搬登入密碼或舊照片；照片與原始檔需另作核對。切換前備份現有 MongoDB、舊照片、Supabase SQL 資料及 Storage 圖片本體，分開加密備份並演練還原。資料庫備份不含 Storage 檔案。[Supabase 備份說明](https://supabase.com/docs/guides/platform/backups)

正式上線前保留原站與備份；若新站失敗，回到原站並保存新站變更以供對帳。不要讓兩站同時接受正式寫入。正式資料不進預覽環境。

## Next.js 回退環境檢查

- 測試與正式使用不同的 Supabase、Vercel 專案及密鑰。正式 URL 使用固定 HTTPS 網址，家人只需入口網帳號；不要給家人雲端管理團隊權限。
- 雲端管理帳號開雙重驗證。入口網目前沒有內建雙重驗證和自助重設密碼；先由管理員核對身分、協助恢復存取。若日後新增自助重設，必須驗證完整流程並設定專用 SMTP；Supabase 預設寄信只允許團隊成員。[寄信說明](https://supabase.com/docs/guides/auth/auth-smtp)
- 切換前須在隔離環境驗證管理員、家人及撤權的存取，並從獨立備份實際還原資料和圖片。每週及重大更新前重做加密備份；正式資料不可用作測試素材。

## 程式結構與測試

- `frontend/src/app`：Next.js 頁面、登入與同源私人 API。
- `frontend/src/components/features` 和 `frontend/src/hooks`：介面、表單及 React Query 請求。
- `frontend/src/lib/hosted`：HTTP 授權、資料契約、PostgreSQL 存取與圖片驗證；`supabase/migrations` 維護資料表和 RLS。
- Node.js 22、pnpm 10.33。CI 會跑測試、lint、CSP、正式建置、公開展示靜態檢查、依賴掃描及供應鏈檢查。歷史 .NET 測試不屬於現行發布門檻。
