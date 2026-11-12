# 許氏入口網開發與部署手冊

[切換至使用者手冊](user.md) · [瀏覽器版](developer.html)

## 私人入口網

預設私人入口網包含家人登入、農曆拜拜規則、活動月曆、成員、行程搜尋與私人照片。只需部署 Next.js 應用程式及 Supabase。舊執行程式已移出工作分支，可由 Git 提交 `5e15500` 取回；這不影響既有部署或資料。在隔離雲端驗收、資料核對及還原演練前不可切換正式站。

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

## 正式環境檢查

- 測試與正式使用不同的 Supabase、Vercel 專案及密鑰。正式 URL 使用固定 HTTPS 網址，家人只需入口網帳號；不要給家人雲端管理團隊權限。
- 雲端管理帳號開雙重驗證。入口網目前沒有內建雙重驗證和自助重設密碼；先由管理員核對身分、協助恢復存取。若日後新增自助重設，必須驗證完整流程並設定專用 SMTP；Supabase 預設寄信只允許團隊成員。[寄信說明](https://supabase.com/docs/guides/auth/auth-smtp)
- 切換前須在隔離環境驗證管理員、家人及撤權的存取，並從獨立備份實際還原資料和圖片。每週及重大更新前重做加密備份；正式資料不可用作測試素材。

## 程式結構與測試

- `frontend/src/app`：Next.js 頁面、登入與同源私人 API。
- `frontend/src/components/features` 和 `frontend/src/hooks`：介面、表單及 React Query 請求。
- `frontend/src/lib/hosted`：HTTP 授權、資料契約、PostgreSQL 存取與圖片驗證；`supabase/migrations` 維護資料表和 RLS。
- Node.js 22、pnpm 10.33。CI 會跑測試、lint、CSP、正式建置、公開展示靜態檢查、依賴掃描及供應鏈檢查。歷史 .NET 測試不屬於現行發布門檻。
