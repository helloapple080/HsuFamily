# 許氏入口網安全改善報告

## 摘要

本次已處理先前列為 Critical／High 的可直接修正項目：瀏覽器 Token 儲存、Refresh Token 明文、Token 重放偵測、Cookie CSRF、SignalR 未授權存取、MongoDB 對外暴露與過大資料庫權限，以及缺少安全測試。前端已通過 TypeScript、ESLint 與 production build；後端因本機沒有 .NET SDK，改由 GitHub Actions 執行 .NET 10 build 與測試。

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

## 尚待處理的中等風險

- 將 AuthController 的登入與 token rotation 業務邏輯移至 Application Service，以完全符合專案 Clean Architecture。
- 在 Next.js response headers 或正式 edge proxy 設置完整 CSP。
- 正式部署時配置 TLS termination、forwarded headers 與可觀測性。
- 為 Controller＋MongoDB 增加整合測試，並以 Playwright 驗證真實登入、refresh、logout 流程。
