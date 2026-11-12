# 許氏入口網開發與部署手冊

[切換至使用者手冊](user.md) · [瀏覽器版](developer.html)

## 精簡版開發分支

`codex/supabase-portal` 的精簡版包含家人登入、農曆拜拜規則、活動月曆、成員、行程搜尋與私人照片。只需部署 Next.js 應用程式及 Supabase，不必運行 .NET 或 MongoDB。原版仍保留；在隔離雲端驗收、資料核對及還原演練前不可切換正式站。

私人家用可先用 Vercel Hobby 搭配 Supabase Free，不需買網域。Supabase Free 含 500 MB 資料庫與 1 GB 檔案空間，低活動約一週可能暫停。照片、流量與備份仍需管理，免費層不是可用性保證。[Vercel 方案](https://vercel.com/docs/plans) · [Supabase 費率](https://supabase.com/pricing)

### 啟動隔離預覽

1. 建立獨立的 Supabase 測試專案，只使用虛構資料。在確認專案 ID 後，按檔名順序套用 `supabase/migrations/` 下的 SQL（從 `202611110001_portal_foundation.sql` 到 `202611120003_calendar_events.sql`）。已手動套用的遷移不要重跑。照片政策需要 Supabase Storage 提供 `storage.allow_any_operation`。
2. 關閉公開註冊，透過 Supabase Dashboard 的 Authentication 建立兩個測試帳號，確認信箱狀態。用各自的 Auth UUID 在 SQL Editor 授權（下例 UUID 必須換成該專案的測試帳號 UUID）：

```sql
insert into public.portal_access (user_id, role) values
  ('<測試管理員 UUID>', 'Admin'),
  ('<測試家人 UUID>', 'User');
```

3. 本機在 `frontend/.env.local` 設定下列變數後執行 `pnpm dev`。Vercel 預覽用獨立專案，Root Directory 設為 `frontend`，使用固定 HTTPS 網址作為 `PORTAL_ORIGIN`（不加結尾斜線，不使用每次部署都不同的網址）。Supabase 只用 Publishable key，不用 service-role 或 secret key。

```dotenv
NEXT_PUBLIC_PORTAL_MODE=supabase
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

登入與資料使用同源 `/api/hosted`；API 和 RLS 各自檢查角色。照片走私人桶，伺服器驗證實際格式、縮圖成 WebP 並去除 EXIF 後，才發出限帳號、檔名與期限的上傳憑證。Storage 不與應用資料共用交易；若並行刪除造成殘留物件，普通家人仍讀不到，應在測試專案驗證補償與管理員清理流程。不得用 SQL 刪除 Storage 物件。公開展示版不包含私人 API；不設 `NEXT_PUBLIC_PORTAL_MODE` 則保留舊版 API。[Storage 權限](https://supabase.com/docs/guides/storage/security/access-control)

在 `frontend` 執行 `pnpm test`、`pnpm lint`、`pnpm audit:prod`、`NEXT_PUBLIC_PORTAL_MODE=supabase pnpm build`，另執行 `pnpm build:pages` 和 `pnpm test:pages`。RLS 測試使用嵌入式 PostgreSQL；登入和照片測試用真實 SDK 搭配模擬服務。這些測試不能取代託管 Auth 與 Storage 的驗收。

### 搬遷、備份與回復

先用[離線搬遷與還原手冊](../superpowers/notes/2026-11-12-legacy-converter.md)轉換假資料，核對成員、行程、月曆、關聯及筆數。工具不搬登入密碼或舊照片；照片與原始檔需另作核對。切換前備份現有 MongoDB、舊照片、Supabase SQL 資料及 Storage 圖片本體，分開加密備份並演練還原。資料庫備份不含 Storage 檔案。[Supabase 備份說明](https://supabase.com/docs/guides/platform/backups)

正式上線前保留原站與備份；若新站失敗，回到原站並保存新站變更以供對帳。不要讓兩站同時接受正式寫入。正式資料不進預覽環境。

## 1. 架構與成本

以同一台主機執行 Docker Compose。外部請求由 Cloudflare Tunnel 進入 Caddy。Caddy 將網頁送往 Next.js，將 `/api/*` 和 `/hubs/*` 送往 .NET。瀏覽器使用同一個 HTTPS 網域，API 和 SignalR 共用現有登入 Cookie。

- **前端**：Next.js 16 App Router、TypeScript、TanStack Query、Zustand。正式版使用 standalone build。
- **後端**：.NET 10。Domain 定義領域模型，Application 處理用例和介面，Infrastructure 實作 MongoDB、媒體儲存與登入等服務，API 負責 HTTP 與組裝。
- **資料**：MongoDB 儲存成員、拜拜、行程、登入與圖片中繼資料。資料庫與 Data Protection keys 放在持久 volume。既有 MongoDB 先備份，再以測試還原確認相容性。
- **照片**：功能分支已有 Google Drive 私有儲存與上傳底層。頁面相簿整合仍待完成。一般部署保持 `GOOGLE_DRIVE_ENABLED=false`。R2 可透過新增 `IMediaStorage` 實作接入，切換前須驗證照片權限、資料遷移與回復。
- **費用**：已有長開主機、網路與網域時，Cloudflare Tunnel 無須加購月費。主機電費、備份空間、網域註冊與續費取決於現有資源。Firebase/D1 會增加 MongoDB 搬遷與重驗成本。

[Cloudflare Tunnel](https://developers.cloudflare.com/tunnel/get-started/) · [R2 定價](https://developers.cloudflare.com/r2/pricing/)

## 2. 本機開發

準備 .NET 10 SDK、Node.js 22、pnpm 10.33 與 MongoDB 7。使用獨立的開發資料庫。從 `.env.example` 建立開發用 `.env`，填入 MongoDB 密碼、JWT 簽章金鑰與初始管理員密碼。開發容器預設綁 loopback。

```sh
docker compose up -d --build
docker compose ps
```

瀏覽器開啟 `http://localhost:3000`。API 預設在 `http://localhost:5001`。若改用 `pnpm dev` 或直接啟動 API，先停止占用對應連接埠的開發容器；自行設定主機上的開發資料庫連線，不可使用正式設定。

平時可從 `frontend` 執行 `pnpm dev`、`pnpm lint`、`pnpm test`、`pnpm build`。在 `backend` 執行 `dotnet build FamilyPortal.slnx` 與 `dotnet test --project FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj`。CI 的版本、依賴掃描與建置設定位於 `.github/workflows/ci.yml`。

新增功能時，先在 Application 定義用例和儲存介面，再由 Infrastructure 實作，API Controller 處理 HTTP、驗證與授權。前端在 `frontend/src/components/features` 維護功能元件，使用 API client 與 React Query。新增資料寫入時，驗證成員與管理員權限、Cookie／CSRF 流程、資料庫索引及資料相容性。

## 3. 準備正式主機

1. 使用可長開且有出站網路的主機，安裝 Docker Engine 和 Docker Compose v2.24.4 以上。磁碟要容納資料庫、映像與備份。確認容器網段與主機/VPN 不衝突。
2. 先決定 Compose 專案名與資料來源。若沿用既有 Compose volume，保持原專案名並核對實際 volume 名稱。跨主機或跨專案名時，先從備份還原到獨立環境。避免把新建空 volume 認成資料搬遷成功。
3. 從 `.env.example` 建立正式機的 `.env` 並限制檔案讀取權限。填入 MongoDB 管理和應用密碼、隨機 JWT 簽章金鑰、一次性初始管理員密碼。不要把真實設定、備份或憑證放入 Git。
4. `PORTAL_DOMAIN` 與 `API_DOMAIN` 皆填同一個主機名，例如 `family.example.com`，不含 scheme。Tunnel 模式只使用這個主機名。
5. 在 Cloudflare 建立具名 Tunnel。把 connector token 放進正式機的 `CLOUDFLARE_TUNNEL_TOKEN`。在 Published applications 將該網域指到 `http://caddy:8080`。若設定 HTTP Host Header，使用相同的正式主機名。啟用對外 HTTP 轉 HTTPS，並為家族入口網主機名設定 Cache Bypass，避免 CDN 快取私人回應。

## 4. 啟動與驗收

從專案根目錄執行以下指令。用 `config --quiet` 驗證插值，避免把完整含秘密的 Compose 配置印到終端或 CI。

```sh
docker compose -f docker-compose.production.yml -f docker-compose.tunnel.yml config --quiet
docker compose -f docker-compose.production.yml -f docker-compose.tunnel.yml up -d --build
docker compose -f docker-compose.production.yml -f docker-compose.tunnel.yml ps
```

- 後端狀態應為 healthy。Tunnel dashboard 應顯示 connector 已連線。Docker 應只在容器網路提供服務，主機無對外發布的容器 port。
- 用 HTTPS 開啟正式網域並登入。匿名請求 `/api/auth/me` 應回傳 401。檢查登入 Cookie 含 Secure 與 HttpOnly。普通成員可閱讀，直接呼叫管理寫入 API 應被拒絕。測試資料應使用獨立帳號和資料庫。
- 用兩個已登入的瀏覽器測試行事曆同步，查看 `/hubs/calendar` 的 WebSocket 連線。斷線重連後核對畫面資料。
- 圖片功能啟用後，確認管理員能上傳 10 MiB 內的有效靜態圖片、超額與不合法檔案遭拒，未登入者無法讀取私人照片。正式家庭資料預設維持私人。
- 確認資料筆數和原主機一致、驗證備份可還原，並保存本次驗收的 commit SHA、時間和負責人。

## 5. 資料備份、升級與故障處理

- 定期匯出 MongoDB 資料，以加密方式存到不同裝置或備份服務。首次上線與升級前建立備份。資料還原應先在隔離環境驗證，核對文件筆數、索引、登入和行事曆。
- 保留 Data Protection keys volume。若更換主機，依安全政策備份、轉移金鑰與設定。Drive 照片本體需另行備份，MongoDB 備份只含其中繼資料。
- 上線時記下已驗證的 Git commit 和容器版本。升級失敗可重新部署先前版本。涉及資料格式變更時，先核對相容性和還原程序。避免執行 `docker compose down -v`，它會刪除資料 volumes。
- 網站 502 時，先看 Cloudflare connector、Caddy 與 API 健康狀態。圖片上傳 413 時，確認命中 `POST /api/media` 專用規則及 API 的 10 MiB 檔案限制。登入重導迴圈時檢查 HTTPS、正式 Host 和可信代理設定。
- Tunnel token 疑似外洩時，在 Cloudflare 輪替並重啟 connector。日常監控應包括連線狀態、API 錯誤、磁碟空間和備份完成率。

以下範例在正式主機產生一份僅目前使用者可讀的備份。請再加密及移到異地備份位置。備份時間應選在暫停資料寫入或資料異動少的時段；需要一致性快照時，安排停寫。

```sh
umask 077
docker compose -f docker-compose.production.yml -f docker-compose.tunnel.yml \
  exec -T mongodb sh -c 'exec mongodump --username familyportal_root --password "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin --db FamilyPortal --archive --gzip' \
  > "familyportal-$(date +%Y%m%d-%H%M%S).archive.gz"
```

還原演練只使用隔離的測試主機及全新測試 volume。執行前用 `docker context show` 與 `docker compose ps` 核對目標，不可連到正式資料庫。先只啟動 MongoDB 並等候健康檢查通過，避免 API 先寫入初始資料。還原後再啟動測試入口網，不連正式 Tunnel，核對資料、索引與登入。

```sh
docker compose -f docker-compose.production.yml -f docker-compose.tunnel.yml up -d --wait mongodb
docker compose -f docker-compose.production.yml -f docker-compose.tunnel.yml \
  exec -T mongodb sh -c 'exec mongorestore --username familyportal_root --password "$MONGO_INITDB_ROOT_PASSWORD" --authenticationDatabase admin --archive --gzip --nsInclude "FamilyPortal.*"' \
  < familyportal-YYYYMMDD-HHMMSS.archive.gz
```

## 6. 維護文件

`docs/guides/*.md` 是來源。修改後執行 `ruby docs/guides/render.rb` 更新兩個免連網 HTML。執行 `ruby docs/guides/render.rb --check` 檢查輸出是否與 Markdown 同步。

[原部署與維運手冊](../04-部署與維運手冊.md) · [架構文件](../02-系統與架構全覽.md)
