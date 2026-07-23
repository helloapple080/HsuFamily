# 許氏入口網安全改善報告

## 摘要

本次已處理先前列為 Critical／High 及可由程式碼消除的中等風險，包括 Access Cookie 過期後無法 refresh、多分頁輪替競態、固定開發密碼、代理來源信任、Data Protection 金鑰持久化、容器 root、錯誤資訊洩漏、敏感日誌、Next.js 安全標頭、浮動套件版本與既有 MongoDB volume 遷移。前端已通過 TypeScript、ESLint 與 production build；後端由 GitHub Actions 執行 .NET 10 build 與測試。

## 已解決項目

### SEC-001 — Critical — 瀏覽器保存 JWT

- 原因：Access／Refresh Token 原本寫入 `localStorage`，可被同 origin 的惡意 JavaScript 讀取。
- 修正：Token 改由 API 寫入 `HttpOnly`、`SameSite=Strict` Cookie，Production 加上 `Secure`；Zustand 只保存記憶體中的非敏感使用者狀態。
- 證據：`frontend/src/stores/authStore.ts:23`、`frontend/src/lib/api.ts:54`、`backend/FamilyPortal.Api/Controllers/AuthController.cs:298`。
- 狀態：已解決。

### SEC-002 — High — Cookie 認證缺少 CSRF 防護

- 原因：改用 Cookie 後，所有寫入請求都必須具備 antiforgery 驗證。
- 修正：ASP.NET Core 全域套用 `AutoValidateAntiforgeryTokenAttribute`，前端僅在記憶體保存 request token，所有 POST／PUT／PATCH／DELETE 自動加入 `X-XSRF-TOKEN`。
- 證據：`backend/FamilyPortal.Api/Program.cs:93`、`frontend/src/lib/api.ts:28`。
- 狀態：已解決。

### SEC-003 — High — Refresh Token 明文保存

- 原因：資料庫外洩時，原始 Refresh Token 可直接被使用。
- 修正：MongoDB 僅保存 SHA-256 hash；原始 Token 只在產生時寫入 HttpOnly Cookie。
- 證據：`backend/FamilyPortal.Domain/ValueObjects/RefreshToken.cs:11`、`backend/FamilyPortal.Infrastructure/Security/JwtTokenService.cs`。
- 狀態：已解決。

### SEC-004 — High — Refresh Token Rotation 無法偵測重放

- 原因：原設計覆蓋舊 Token，無法辨識已被使用的 Token 再次出現。
- 修正：保存 token family 輪替歷史；原子輪替失敗或已撤銷 Token 再次使用時，撤銷整個 family。
- 證據：`backend/FamilyPortal.Api/Controllers/AuthController.cs:141`、`backend/FamilyPortal.Infrastructure/MongoDB/UserRepository.cs`。
- 狀態：已解決。

### SEC-005 — High — MongoDB 對主機公開且應用程式使用 root

- 原因：MongoDB `27017` 原本發布到主機，Backend 使用 root 憑證。
- 修正：取消發布 MongoDB port；初始化獨立 `familyportal_app` 帳號，只授予 `FamilyPortal` 的 `readWrite`，並加入正確 `authSource`。
- 證據：`docker-compose.yml:7`、`docker-compose.yml:25`、`backend/scripts/mongo-init.js`。
- 狀態：已解決。部署前必須在本機 `.env` 設定新的 `MONGO_APP_PASSWORD`。

### SEC-006 — High — SignalR Hub 可匿名連線

- 原因：Hub 未標註授權，家庭事件通知可能被匿名訂閱。
- 修正：Hub 類別及 endpoint 都要求登入，並透過 HttpOnly access cookie 驗證。
- 證據：`backend/FamilyPortal.Infrastructure/Hubs/CalendarHub.cs`、`backend/FamilyPortal.Api/Program.cs:242`。
- 狀態：已解決。

### SEC-007 — High — 缺少安全回歸測試

- 修正：新增 xUnit v3 測試，涵蓋 Refresh Token 只保存 hash、token family、JWT user ID 與 Argon2id；GitHub Actions 執行完整 solution build、test 與 coverage collector。
- 證據：`backend/FamilyPortal.Security.Tests/`、`.github/workflows/ci.yml:23`。
- 狀態：已解決基礎安全測試；完整登入 E2E 仍應在可啟動 MongoDB／API 的環境補上。

## 追加改善（2026-07-23）

- Refresh 僅以雜湊後的 Refresh Token 查找使用者，不再要求瀏覽器已刪除的過期 Access Cookie。
- Web Locks 將跨分頁 refresh 序列化；等待者先驗證共享 Cookie，避免將正常競態誤判成重放攻擊。
- Cookie `Secure` 改成明確設定；正式環境預設安全，Docker 本機 HTTP 必須明確選擇非安全模式。
- 僅接受明列 `KnownProxies` 的 forwarded headers；Data Protection keys 使用具名 volume 保存。
- Backend 以非 root 使用者執行；Docker 與 IdentityModel 版本固定。
- Mongo app user 每次部署冪等建立／更新，既有 volume 不再漏掉首次初始化腳本。
- Next.js 加入 CSP、Permissions Policy、COOP、frame／MIME／referrer 防護。
- 新增 API 整合測試，確認匿名健康檢查、不帶 Cookie 的授權拒絕及 CSRF 拒絕。

## 部署端仍須完成的責任

- 對外環境必須由 TLS reverse proxy 提供 HTTPS，設定 `COOKIE_SECURE=true`、實際 `ALLOWED_HOSTS`、`AllowedOrigins` 與代理 IP；缺少實際網域／代理拓撲，程式碼無法代替此設定。
- 密碼與 JWT secret 必須由 secret manager 或環境變數注入並定期輪替，不得提交到 Git。
- CSP 為相容 Next.js hydration 暫時允許 inline script/style；若要達到更嚴格政策，部署層應導入逐請求 nonce。
- 真實瀏覽器登入 E2E 需要可啟動的 MongoDB、API 與測試帳號；目前 CI 先覆蓋 API 安全邊界與 production build。
