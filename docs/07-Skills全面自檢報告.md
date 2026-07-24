---
title: 許氏入口網 Skills 全面自檢報告
tags: [HsuFamily, audit, security, architecture, uiux, skills]
updated: 2026-07-24
code_baseline: 40fd5549c1da98fdcdee9d5ad64a5cd98206911b
---

# 許氏入口網 Skills 全面自檢報告

## 管理結論

專案**具備企業級工程思維，但目前不能宣稱企業正式上線就緒**。

正向基線已成立：後端採預設拒絕授權、Admin RBAC、Argon2id、HttpOnly／SameSite Cookie、CSRF、Refresh Token hash／rotation／replay detection、CORS allowlist、rate limit、非 root container 與 MongoDB 最小權限；前端 TypeScript strict、集中 API client、React Query、Zod、production build 與基本響應式版面均可運作。

然而 2026-07-20 新發布的 Next.js 安全更新，已使目前鎖定的 `next 16.2.2` 產生已知 High 風險；即時 `pnpm audit` 也辨識出 `axios 1.14.0` 的 High advisories。加上 production compose 預設使用 HTTP 且 `COOKIE_SECURE=false`，目前應先暫停正式公開上線，完成 P0 修復後再驗收。

### 本次判定

| 項目 | 結論 |
|---|---|
| Critical | 0 個已確認 |
| High | 3 個需優先處理 |
| Medium | 13 組改善項目 |
| Low | 4 組維護項目 |
| ESLint | 通過，0 error |
| Next.js production build | 通過，8 個 prerender tasks 完成 |
| 遠端 CI | commit `40fd554`、run `30029122350` 通過 |
| 依賴弱點掃描 | 未通過：58 筆 advisory 命中（26 High／28 Moderate／4 Low，主要集中於直接依賴 Next.js、Axios） |
| 瀏覽器驗證 | 登入頁桌機／375px 通過；行事曆 375px 無水平溢位，但有觸控、語意與文化內容缺口 |

> `58` 是 registry 回傳的 advisory 命中數，不等於 58 條都能在本專案利用。部分 Axios 項目只影響 Node HTTP adapter；但直接依賴版本已有官方修補版，仍應依企業弱點 SLA 升級並重新測試。

## 套用的 Skills 與實際用途

| Skill | 本次實際檢查 |
|---|---|
| `using-superpowers` | 依 skill-first 流程選擇並套用專案相關能力 |
| `verification-before-completion` | 以最新命令輸出與瀏覽器證據取代主觀宣稱 |
| `code-review` | 架構、可維護性、驗證與測試覆蓋檢查 |
| `aspnet-core` | Middleware order、認證授權、Cookie、CSRF、HSTS、rate limiting、health check |
| `security-best-practices` | Next.js／React secrets、XSS、CSP、cookies、CSRF、供應鏈與輸入邊界 |
| `mongodb-query` | 索引、游標分頁、查詢上限與文件成長風險 |
| `signalr-realtime` | Hub 授權、自動重連、連線狀態與重同步 |
| `error-handling` | API 安全錯誤、前端 error／loading／recovery UX |
| `lunar-calendar` | 國農曆轉換、閏月、格式與台灣文化內容 |
| `nextjs-component` | App Router、Server／Client boundary、React Query 與元件規範 |
| `ui-ux-pro-max` | 以 design-system、UX、Next.js 資料庫檢查鍵盤、focus、44px、reduced motion、錯誤恢復與 emoji icon |
| `playwright` | 實際 production 頁面、桌機／375px、DOM、觸控尺寸、reduced-motion 與受保護頁 mock API 驗證 |

本次不是只確認 skills 已安裝：`ui-ux-pro-max/scripts/search.py` 實際執行了 design system、accessibility 與 Next.js 查詢；Playwright 實際啟動 production build 並檢查登入與行事曆頁。

## 已確認做得好的部分

- `Program.cs:100-103` 使用 fallback authorization policy，未公開標註的 endpoint 預設必須登入。
- 所有寫入 CRUD 皆有 `AdminOnly`；SignalR Hub 類別與 endpoint 皆需授權。
- `AuthController.cs:291-308` 使用 `HttpOnly`、`SameSite=Strict`，Refresh Cookie 限縮到 `/api/auth`。
- `Program.cs:117-130` 全域驗證 cookie-authenticated unsafe methods 的 antiforgery token。
- Refresh Token 原文不進 DB；具 rotation、family 與 replay 撤銷；原子更新可處理競態。
- JWT secret、Mongo／Admin 密碼未硬編碼；必要 secret 缺失時啟動即失敗。
- 未找到 `dangerouslySetInnerHTML`、直接 DOM HTML sink、`eval`、Web Storage token、`.Result`／`.Wait()` 或敏感 Token／密碼日誌。
- CORS 使用明確 origin 並搭配 credentials，未使用 wildcard origin。
- MongoDB 不發布 27017、app user 為單 DB `readWrite`；container runtime 使用非 root 帳號。
- TypeScript `strict: true`，未找到 `any` 或元件直接 `fetch`；API 集中由 Axios client 與 React Query 管理。
- 實測 response headers 包含 CSP、nosniff、frame denial、referrer、permissions、COOP。
- 375px 登入頁與行事曆沒有水平溢位，登入主要 CTA 為 44px 高。
- MongoDB 7.0 仍在官方支援期至 2027-08-31；不必為了追逐版本立即重寫資料層。

## High findings

### AUD-DEP-001 — Next.js 已知 High 弱點

- **Rule ID：** NEXT-SUPPLY-001
- **Severity：** High
- **Location：** `frontend/package.json:31,48`、`frontend/pnpm-lock.yaml`（實際 `next@16.2.2`）
- **Evidence：** 2026-07-20 Next.js 官方要求 Active LTS 升至 `16.2.11`，處理 4 High／5 Medium；`pnpm audit --prod` 亦列出多個 DoS、proxy bypass、SSRF、cache confusion advisories。
- **Impact：** 自架 Next server 可能受可用性、代理路由、快取或 Server Component 類弱點影響；實際利用面依本專案未使用的功能而異，但版本本身已不符合企業安全基線。
- **Fix：** 升級 `next` 與 `eslint-config-next` 至至少 `16.2.11`，重新執行 lint、build、Playwright 與完整 dependency audit。
- **Mitigation：** 修補前不可直接暴露公共網路；以 reverse proxy 限制 request size、connection、rate 與 timeout。
- **False-positive notes：** 專案未使用 Server Actions、Cache Components 或 Next Middleware，部分 CVE 路徑可能不可達；這只能降低可利用性，不能取代受支援 patch。

### AUD-DEP-002 — Axios 直接依賴有已知 High advisories

- **Rule ID：** REACT-SUPPLY-001
- **Severity：** High（套件層）；本專案可利用性需在升級後再確認
- **Location：** `frontend/package.json:24`、`frontend/pnpm-lock.yaml`（實際 `axios@1.14.0`）
- **Evidence：** 即時 npm audit 指出 credential injection、request hijacking、prototype pollution 等 advisories；回報的安全版本包含 `>=1.18.0`。
- **Impact：** 若未來加入 Node adapter、動態 request options 或受污染物件，風險會擴大；目前固定 `baseURL` 且主要在 browser 使用，降低部分 HTTP-adapter 項目的可利用性。
- **Fix：** 升至 audit 建議的安全版本，檢查 breaking changes，回歸 cookie、CSRF、refresh lock 與 SignalR 共存流程。
- **Mitigation：** 維持固定 base URL、禁止使用者控制 request destination 或合併不可信 config。
- **False-positive notes：** 不能把所有 1.14.0 advisories 都視為目前瀏覽器路徑可利用；仍必須升級並讓 audit 歸零或建立有期限的例外文件。

### AUD-DEPLOY-001 — Production compose 預設形成 HTTP + 非 Secure Cookie

- **Rule ID：** NEXT-SESS-001／ASP.NET deployment baseline
- **Severity：** High（若照預設公開部署）
- **Location：** `docker-compose.yml:48-67,86-90`
- **Evidence：** `ASPNETCORE_ENVIRONMENT=Production`、API 綁定／發布 `http://+:5001`，`SecuritySettings__CookieSecure=${COOKIE_SECURE:-false}`，前端 API URL 也預設 `http://localhost:5001`。
- **Impact：** 若 compose 未置於正確 TLS reverse proxy 後方，access／refresh cookie 可透過明文 HTTP 傳輸，且後端埠直接暴露主機。
- **Fix：** 分離 `compose.dev.yml` 與 production deployment；Production 預設 `COOKIE_SECURE=true`，只由 TLS proxy 對外，API port 僅內網／loopback，設定 HSTS 與可信 proxy。
- **Mitigation：** 部署 pipeline 加入 fail-fast：Production + `CookieSecure=false` 或缺少 canonical HTTPS origin 時拒絕啟動。
- **False-positive notes：** 若此 compose 明確只供 localhost 開發，實際外部風險較低；目前檔案本身沒有防止被當成 production 配置。

## Medium findings

### AUD-SDLC-001 — CI 綠燈未包含依賴、容器或程式碼安全掃描

- **Rule ID：** REACT-SUPPLY-001
- **Severity：** Medium
- **Location：** `.github/workflows/ci.yml:23-61`
- **Evidence：** CI 只執行 restore、build、test、lint；沒有 `pnpm audit`、NuGet vulnerable/deprecated、CodeQL、container scan 或 SBOM。現有 CI 在相同 commit 通過，但即時 audit 已失敗。
- **Impact：** 新 advisory 發布後，沒有程式碼變更也會從安全變成不安全，綠燈無法阻擋發布。
- **Fix：** 加入 scheduled + PR supply-chain gates、Dependabot／Renovate、CodeQL、Trivy／Grype 與 CycloneDX／SPDX SBOM；定義 High 修復 SLA。
- **Mitigation：** 每次 release 前手動留下 audit artifact。
- **False-positive notes：** GitHub repository settings 可能另有 Dependabot／secret scanning，本機 repo 無法證明；需至 repository security settings 驗收。

### AUD-RUNTIME-001 — .NET security patch 與 HSTS 基線落後

- **Rule ID：** ASP.NET deployment/operations
- **Severity：** Medium
- **Location：** `backend/Dockerfile:2,15`、`FamilyPortal.Api.csproj`、`Program.cs:246-262`
- **Evidence：** build SDK 已是 `10.0.302`，但 runtime image 為 `10.0.9`；Microsoft 2026-07-14 已發布 `10.0.10` security patch。JWT/OpenAPI packages 仍為 `10.0.5`。Pipeline 只有 HTTPS redirection，未見 `UseHsts()`。
- **Impact：** 遺漏最新 runtime／ASP.NET fixes；若無 edge HSTS，首次連線仍可能被降級。
- **Fix：** 對齊 runtime 與 Microsoft packages 到當期安全 patch，production 啟用 HSTS；驗證 proxy 正確傳遞 scheme。
- **Mitigation：** 在 edge 強制 HTTPS/HSTS 並以 image digest 固定核准映像。
- **False-positive notes：** edge 可能已加 HSTS，但 repo 沒有證據；需以正式 URL response header 驗收。

### AUD-AUTH-001 — Refresh Token history 在單次 session 內無上限

- **Rule ID：** Session boundedness / Mongo document growth
- **Severity：** Medium
- **Location：** `UserRepository.cs:51-72`、`User.cs:41-43`
- **Evidence：** 每次 rotation 都 `.Push()` 新 token，舊的 revoked/expired history 不 prune；refresh policy 允許每 IP 每 5 分鐘 30 次。
- **Impact：** 長時間或濫用 refresh 可持續擴大單一 User 文件，增加查詢／更新成本並接近 MongoDB 16MB document limit。
- **Fix：** rotation 時原子 prune 過期紀錄並限制 history；更完整做法是 session/token 獨立 collection + TTL index + per-user active session cap。
- **Mitigation：** 降低 refresh rate、監控 user document size 與 refresh frequency。
- **False-positive notes：** 每次重新登入會 Replace 整個 token list，且 production refresh lifetime 為 7 天，所以一般家庭使用量下風險低；惡意或自動化呼叫仍可放大。

### AUD-INPUT-001 — 查詢與文件欄位缺少明確資源上限

- **Rule ID：** NEXT-INPUT-001 / resource controls
- **Severity：** Medium
- **Location：** `ItinerariesController.cs:64-85`、`ItineraryRepository.cs:57-75`、`EventsController.cs:38-45,66-88`、各 Upsert DTO／Value Objects
- **Evidence：** search `limit` 未設 `[Range]`／clamp；participant/type/date-range queries 無分頁或最大期間；Event description、tags、member IDs、Itinerary lists/text、Member bio/contact、LunarDate month/day 等缺少 length/count/range 驗證。
- **Impact：** 已登入使用者可發出昂貴查詢；Admin 可建立大型文件或不合法農曆資料，造成效能、儲存與資料品質問題。
- **Fix：** 建立一致 validation constants；限制查詢 1–100、日期範圍例如 62 天、文字長度、陣列數量與元素長度，所有 list endpoint 分頁；Lunar month 1–12、day 1–30、recurrence interval > 0。
- **Mitigation：** reverse proxy request body limit、API timeout、Mongo slow-query monitoring。
- **False-positive notes：** 寫入限 Admin 降低外部攻擊面，但不能取代資料完整性與文件大小控制。

### AUD-ARCH-001 — Clean Architecture 規則尚未真正落地

- **Rule ID：** Project AGENTS architecture rules
- **Severity：** Medium
- **Location：** `EventsController.cs:5,21-31`、`AuthController.cs:6,24-47`、`Program.cs:6-9,278-313`；`FamilyPortal.Application` 無 Service 層
- **Evidence：** API Controllers 直接引用 Infrastructure 的 `CalendarHub`／`JwtSettings`，並承擔 login、rotation、mapping、entity mutation、SignalR publishing 等業務流程。
- **Impact：** HTTP、業務規則與基礎設施耦合，使測試、替換傳輸層與擴充授權更困難；不符合專案自己宣告的「所有業務邏輯在 Application Service」。
- **Fix：** 建立 `IAuthService`、`IEventService`、`IMemberService`、`IItineraryService` 與 notification abstraction；Controller 只處理 HTTP／DTO；Infrastructure 實作 token、repository、real-time publisher。
- **Mitigation：** 先從 328 行的 AuthController 抽離，加入 Application service unit tests。
- **False-positive notes：** composition root 引用 Infrastructure 是合理例外；問題是 Controller 直接依賴與業務邏輯位置，不是單純 project reference。

### AUD-PIPE-001 — 使用者型 rate-limit partition 實際上只會看到 IP

- **Rule ID：** ASP.NET middleware order
- **Severity：** Medium
- **Location：** `Program.cs:186-220,258-262`
- **Evidence：** limiter partition 先讀 `context.User.Identity.Name`，但 `UseRateLimiter()` 排在 `UseAuthentication()` 之前。
- **Impact：** 已登入者仍按 IP 分桶；同 NAT 家庭成員可能互相限流，單一使用者也能跨 IP 分散。這不是授權繞過，但與政策設計不一致。
- **Fix：** 需要 user identity 的 limiter 應在 authentication 之後執行，並以 immutable user ID claim 分區；login 仍按可信 proxy 後的 client IP。
- **Mitigation：** 保留 edge IP throttling 作雙層限制。
- **False-positive notes：** login／refresh 本來就設計成 IP partition，不受此問題；影響一般 `fixed` policy。

### AUD-WEB-001 — CSP 仍允許 inline script 且洩漏框架標頭

- **Rule ID：** NEXT-CSP-001／REACT-CSP-001
- **Severity：** Medium
- **Location：** `frontend/next.config.ts:3-14`
- **Evidence：** runtime header 為 `script-src 'self' 'unsafe-inline'`；另回傳 `X-Powered-By: Next.js`。
- **Impact：** CSP 對 inline XSS 的防護被削弱；框架標頭增加不必要 fingerprinting。
- **Fix：** 採 Next.js nonce/hash CSP，移除 script `unsafe-inline`；設定 `poweredByHeader: false`。style inline 可分階段處理。
- **Mitigation：** 保持 React escaping、禁止 HTML sinks、限制第三方 script；目前已做到。
- **False-positive notes：** 本次掃描未找到 XSS sink，因此這是 defense-in-depth，不代表目前已證實 XSS。

### AUD-UI-001 — 一般使用者仍看到 Admin CRUD 操作

- **Rule ID：** REACT-AUTHZ-001（UX consistency）
- **Severity：** Medium
- **Location：** `calendar/page.tsx:98-102`、`members/page.tsx:37-39,62-70`、`itineraries/page.tsx:64-66,124-132`
- **Evidence：** `isAdmin` 只用於 layout badge；新增／編輯／刪除 controls 對所有登入者顯示，後端才回 403。
- **Impact：** 一般使用者會遇到可見但不可用的操作與錯誤 toast，降低信任與可用性。
- **Fix：** UI 依 `isAdmin` 隱藏或 disabled 並說明權限；後端 `AdminOnly` 必須保留，不能把前端 gating 當安全控制。
- **Mitigation：** 對 403 提供明確「權限不足」訊息。
- **False-positive notes：** 後端授權完整，所以不是權限繞過。

### AUD-A11Y-001 — 鍵盤語意、觸控尺寸與 reduced-motion 未達基線

- **Rule ID：** UI/UX Pro Max accessibility baseline
- **Severity：** Medium
- **Location：** `PortalLayout.tsx:70-123,215-228`、`calendar/page.tsx:88-102,129-166`、`globals.css:202-224`
- **Evidence：** 桌面 `nav` 無 accessible label／active link `aria-current`；無 skip link/main target；DialogTrigger 渲染的 calendar `DIV` 實測只有 `tabindex=0`、無 `role=button`／日期 accessible name；375px 前後月 controls 寬 27px、menu／新增 32px，高於零但低於 44×44 建議。Playwright 在 `reducedMotion=reduce` 的登入頁仍測得 4 個 active CSS animations。
- **Impact：** screen reader 無法辨識互動類型，鍵盤導航有額外焦點停駐，行動使用者較難點擊，動態效果可能造成不適。
- **Fix：** 使用真正 Button／Link 元件與 Lucide icons；加 skip link、main id、desktop nav label/current；所有觸控 target 至少 44px；CSS `prefers-reduced-motion` 與 Framer `useReducedMotion`。
- **Mitigation：** 在 CI 加 Playwright + axe 與 375/768/1024/1440 viewport checks。
- **False-positive notes：** Base UI 可能仍攔截 Enter/Space 開啟 Dialog，但缺少 role/name 對輔助科技仍是不完整語意。

### AUD-FORM-001 — 表單標籤、錯誤恢復與登入 autocomplete 不完整

- **Rule ID：** UI/UX Pro Max forms/error recovery
- **Severity：** Medium
- **Location：** 三個 feature Dialog、`login/page.tsx:93-125`、`Providers.tsx:23-30`
- **Evidence：** Dialog 內多數 `<Label>` 沒有 `htmlFor`，errors 未以 `aria-describedby`／`aria-invalid` 關聯；query error 多為純文字或 toast，沒有 retry action；全域 query 對 401/403 也固定 retry 2 次；登入 DOM 實測 username/password `autocomplete` 皆空白。
- **Impact：** screen reader 與鍵盤使用者較難理解欄位錯誤；暫時錯誤缺少恢復途徑；auth failure 可能多送重複請求。
- **Fix：** 完整 label/id/error association、inline `aria-live` error summary、`autocomplete=username/current-password`；retry function 對 401/403/validation false，5xx/network 才有限重試；route `error.tsx` 提供 retry。
- **Mitigation：** toast 保留作補充，不作唯一錯誤管道。
- **False-positive notes：** 登入兩個 label 已正確連結；問題主要是 autocomplete 與 Dialog forms。

### AUD-REALTIME-001 — SignalR 重連沒有可見狀態與資料重同步

- **Rule ID：** SignalR realtime reliability
- **Severity：** Medium
- **Location：** `useSignalR.ts:14-45`
- **Evidence：** reconnecting/reconnected/closed 只 `console.log`；`onreconnected` 不 invalidate/refetch calendar data；未回傳 connection state 給 UI。
- **Impact：** 中斷期間漏掉的事件不會自動補回，使用者看不到離線／重連狀態，可能誤以為資料即時且完整。
- **Fix：** hook 暴露 connection state；reconnected 後 invalidate events；UI 顯示重連／離線 banner；加入 reconnect E2E。
- **Mitigation：** 目前 query mount/window policy仍會在部分導航時重新取資料，但不能保證補齊中斷期間事件。
- **False-positive notes：** SignalR automatic reconnect 本身已設定，問題是資料一致性與使用者回饋。

### AUD-LUNAR-001 — 台灣文化內容與農曆生日資料不一致

- **Rule ID：** Lunar calendar cultural correctness
- **Severity：** Medium
- **Location：** `lunar.ts:37-56`、`MemberDialog.tsx:63-86`、`LunarDate.cs:6-21`
- **Evidence：** 直接展示 `solar.getFestivals()`；Playwright 2026 年 7 月畫面實際出現簡中「建党节」，不適合台灣家族入口網。新成員農曆生日送出 `year: 0`、`displayText: ''`，後端直接保存，列表因此可能顯示 `--`。月／日與閏月也沒有完整合法性驗證。
- **Impact：** 產生文化錯置、錯誤顯示與不可用資料；閏月不是每年都有，錯誤輸入可能導致後續轉換失敗。
- **Fix：** 建立台灣節日 allowlist／自訂 festival provider；顯示文字由單一可信層計算而非 client 傳入；驗證 month/day/leap-month/year，補農曆與閏月單元測試。
- **Mitigation：** 修補前不要直接顯示套件內建政治／地區性國曆節日。
- **False-positive notes：** `lunar-javascript` 的陰曆換算本身仍適用；問題是其內建節日資料集與本專案文化情境。

### AUD-TEST-001 — 測試覆蓋不足以支持企業級宣稱

- **Rule ID：** Verification / testing baseline
- **Severity：** Medium
- **Location：** `FamilyPortal.Security.Tests/ApiSecurityIntegrationTests.cs:24-51`；frontend 無 test/spec、無 `error.tsx`／`loading.tsx`
- **Evidence：** API integration 只有匿名 health、未登入拒絕、login 無 CSRF 三條；未測真正 login-cookie-refresh-rotation-replay-logout、Admin/User RBAC、CRUD validation、SignalR auth/reconnect、Mongo indexes；前端無自動測試。
- **Impact：** 高風險流程可在 CI 全綠時回歸，現有 CI 證明「能 build」，不能證明「完整安全行為」。
- **Fix：** Testcontainers Mongo integration、完整 auth/session tests、RBAC matrix、API boundary tests、Playwright E2E、axe、lunar/leap-month tests；設定 coverage threshold 與 test artifact。
- **Mitigation：** release checklist 執行手動 smoke test 並留證據。
- **False-positive notes：** Argon2/JWT 已有 unit tests，是良好開始；此項是覆蓋範圍問題。

## Low findings

- `Program.cs:252` 的 `X-XSS-Protection` 已過時，建議移除，依 CSP／escaping／nosniff 防護。
- startup admin 採「先查再新增」，多 instance 同時啟動可能撞 unique index；應捕捉 duplicate key 或改成 idempotent atomic upsert。
- `next.config.ts` 可關閉 `X-Powered-By`；production response 的公開 static shell 有一年 shared cache，目前未含 user data，風險低但應寫入 cache policy 文件。
- 多個互動區用 emoji 作結構性 icon，與 UI/UX Pro Max 的 Lucide／SVG 規則不一致；也有部分 `confirm()` 與 hover-only destructive actions，不利觸控與一致體驗。

## 建議處理順序

### P0：公開部署前

1. Next.js 升至至少 16.2.11、Axios 升至 audit 安全版本，重新 audit／build／E2E。
2. .NET runtime／Microsoft packages 對齊 10.0.10 security patch。
3. 拆分 dev/prod deployment；正式環境 TLS、Secure Cookie、HSTS、reverse proxy、API 非公開直連。
4. CI 新增依賴、CodeQL、container scan 與 SBOM gate。

### P1：一個迭代內

1. 限制 refresh history、query/date-range/page/DTO/list/string sizes。
2. 修正農曆生日資料與台灣節日來源，補測試。
3. 修正 Admin UI gating、SignalR reconnect resync 與 error recovery。
4. 完成 accessibility：semantic controls、skip link、44px、form relations、reduced motion。

### P2：架構與治理

1. 抽出 Application Services 與 realtime abstraction。
2. 建立 Testcontainers + Playwright 全流程與 coverage gate。
3. 正式監控、集中 log、backup restore、RPO/RTO、SLO、incident drill 與權限審查。

## 驗證紀錄

| 驗證 | 結果 |
|---|---|
| `./node_modules/.bin/eslint .` | PASS |
| `./node_modules/.bin/next build` | PASS；Next.js 16.2.2，8 routes |
| `pnpm audit --prod --audit-level moderate` | FAIL；4 Low／28 Moderate／26 High advisory hits |
| `gh run list` | run `30029122350` PASS，HEAD `40fd554` |
| Playwright login 1440px／375px | PASS，版面正常；autocomplete/reduced-motion 缺口 |
| Playwright calendar 1440px／375px + mock API | 無水平 overflow；確認 27px controls、缺 role、台灣文化錯置 |
| Secret／XSS／Web Storage／blocking async pattern scan | 未發現高風險 pattern |
| Backend local build/test | 本機無 `dotnet`／Docker，改引用相同 HEAD 的 GitHub Actions backend build/test；仍缺本次 NuGet audit |

## 時效性來源

- [Next.js July 2026 Security Release](https://nextjs.org/blog/july-2026-security-release)：要求 Active LTS 升級到 16.2.11。
- [Microsoft .NET 10 downloads](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)：2026-07-14 最新 security patch 為 10.0.10。
- [MongoDB lifecycle](https://www.mongodb.com/legal/support-policy/lifecycles)：MongoDB 7.0 支援至 2027-08-31。
- npm audit advisories：`GHSA-q4gf-8mx6-v5v3`、`GHSA-8h8q-6873-q5fj`、`GHSA-pmwg-cvhr-8vh7`、`GHSA-pf86-5x62-jrwf` 等；應以每次 pipeline 的即時 registry 結果為準。

## 最終聲明

先前文件所稱「Critical／High 已處理」是對 2026-07-23 當時已知程式碼風險的描述；2026-07-20 公告且本次才執行的最新供應鏈掃描，使該結論必須更新。安全是一個持續狀態，不是一次性證書。

目前最準確的說法是：**架構與安全基線方向正確，已有企業級思維；但存在 3 個 High release blockers 與多項 Medium 工程缺口，完成 P0 並取得正式環境證據後，才能重新判定是否可公開上線。**
