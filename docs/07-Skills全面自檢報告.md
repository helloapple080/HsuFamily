---
title: 許氏入口網 Skills 全面自檢報告
aliases: [HsuFamily Skills Audit, 全面自檢報告]
tags: [HsuFamily, audit, security, architecture, uiux, skills]
updated: 2026-07-24
code_baseline: a4ab891
---

# 許氏入口網 Skills 全面自檢報告

## 管理結論

專案已具備**企業級工程思維**，且截至 `a4ab891` 的已知程式碼 Critical／High 安全基線已閉環。先前自檢列出的 Next.js、Axios 與 production deployment blockers 均已修復，不能再列為 open。

這仍不是正式生產認證。正式 DNS／TLS／proxy、Secret Manager、監控告警、備份還原、RPO／RTO、組織權限與獨立安全測試尚未在實際環境取得證據。

## 本次判定

| 項目 | 結果 |
|---|---|
| Open Critical／High code blockers | `0` |
| .NET | SDK `10.0.302`；ASP.NET Core／.NET runtime `10.0.10` |
| Backend tests | `158/158` |
| Frontend tests | `54/54`（12 test files） |
| ESLint | PASS |
| Next.js production build | PASS；Next.js `16.2.11` |
| NuGet vulnerable audit | 5 projects 無已知 vulnerable package |
| pnpm production audit | Critical 0、High 0、Low 1 |
| Code quality gates | minimum backend tests 150、Cobertura artifact、CodeQL、Trivy、SBOM、Dependabot |
| 正式環境認證 | 尚未完成 |

## Skills 套用範圍

| 類別 | 實際使用 | 影響成果 |
|---|---|---|
| Superpowers | planning、worktree、TDD、debugging、review、verification | 以小步驟與證據完成 4 階段 remediation |
| ASP.NET Core／project skills | pipeline、services、DTO、Mongo、SignalR、errors、lunar | Clean Architecture、resource controls、auth／realtime correctness |
| Impeccable／ui-ux-pro-max／ui-styling | UI audit、a11y、forms、states、responsive | Admin gating、error recovery、semantic controls、54 tests |
| Vercel web／React practices | interface guidelines、Next／React performance patterns | App Router、retry／render、CSP 與 UX consistency |
| Emil design engineering | 動效與細節克制 | reduced motion、連線狀態、穩定 micro-interactions |
| Presentations | 可編輯 PPTX／render QA | 既有 10 頁長官簡報；新內容待同步 |

完整選擇理由與未使用項見 [對話需求驗收與 Skills 紀錄](./06-對話需求驗收與Skills紀錄.md)。Anthropic `frontend-design` 與 `taste-skill` 未安裝、未使用；不可寫成已套用。

## 自檢後已關閉的 Findings

### 供應鏈與 runtime

- Next.js 已固定 `16.2.11`；Axios 已進入 `1.18.x` 安全版本線。
- pnpm overrides 修補 High transitive packages；目前 High／Critical 為 0，剩 1 Low 追蹤。
- .NET SDK 固定 `10.0.302`，runtime 與 ASP.NET packages 對齊 `10.0.10`。
- `Microsoft.OpenApi 2.7.5` 修補 CVE-2026-49451。
- `SharpCompress 0.48.0`、`Snappier 1.3.1` 修補 CVE-2026-44302／CVE-2026-44788。
- NuGet 5 projects 掃描未發現已知漏洞。

### Production deployment 與瀏覽器安全

- Development／Production Compose 分離；Production 只有 Caddy 發布 80／443。
- Secure Cookie、HTTPS AllowedOrigins、KnownProxies、AllowedHosts 與必要 env fail-fast。
- Caddy 固定 proxy IP；API／Frontend 只 expose 內網，MongoDB 不發布 port。
- HSTS、CSP per-request nonce、powered-by 關閉、現代 API headers。
- Sonner CSS 由同源載入並用版本 patch 停止無 nonce runtime style injection。

### 後端架構與 boundedness

- `ServiceResult`／`ServiceError` 提供不依賴 HTTP 的 Application contract。
- Event／Member／Itinerary／Auth services 承擔 use cases；Controllers 不再直接引用 Infrastructure。
- Architecture tests 遞迴掃 Controllers 且排除 AppleDouble `._*`。
- 統一限制 page size、date range、文字、tags、participants、photos、recurrence、lunar month／day。
- Refresh history 保留所有 active sessions，inactive history 有界；Mongo query 與 cursor pagination 對齊 indexes。
- middleware 正確順序；一般 limiter 依 NameIdentifier，login／refresh 依 IP。
- Kestrel／Caddy request body 1 MiB。
- Default admin duplicate-safe、錯誤分類可執行測試、collision fail-fast、同一 CancellationToken 傳遞。

### CSRF 與測試基礎設施

- `AddControllersWithViews` 補齊 ASP.NET Core antiforgery 所需服務；unsafe Controller requests 套全域驗證。
- 修正 xUnit v3 MTP runner 假綠：`Microsoft.Testing.Platform`、`xunit.v3.mtp-v2`、`dotnet test --project`。
- CI `--minimum-expected-tests 150`；目前 158 tests。
- coverlet MTP 產生 Cobertura 並上傳 artifact，缺檔即 fail。
- Vitest 排除 AppleDouble，前端 54 tests 覆蓋 recovery、forms、roles、calendar、layout、SignalR。

### 前端 UI／UX、文化與 realtime

- 一般 User 不再看到 Admin CRUD；後端 authorization 仍是最終安全邊界。
- skip link、main landmark、aria-current、form associations、autocomplete、touch target 與 reduced motion 已補齊。
- route ErrorState、retry policy、ConfirmDialog 與 loading／empty／error recovery 更一致。
- 台灣 solar／lunar festival allowlist 移除不適用的簡中政治節日；農曆生日 canonical display 與 validation 一致。
- SignalR 改為共享單例 lifecycle：reference count、StrictMode remount、auth transition、reconnect state、cache resync 與手動重連。

## 仍開啟的 Findings：環境／治理

| Finding | 等級定位 | 必要證據 |
|---|---|---|
| 正式 TLS／DNS／proxy／headers | 上線 Gate | 正式 URL 外部 scan 與 proxy 設定紀錄 |
| Secrets lifecycle | 上線 Gate | Secret Manager、rotation、break-glass／revoke 演練 |
| Backup／restore／RPO／RTO | 上線 Gate | 實際 restore report 與業務核准 |
| Observability／SLO／on-call | 營運成熟度 | dashboards、alerts、runbook、演練 |
| 正式 auth／RBAC／CRUD／SignalR E2E | QA Gate | Playwright／smoke artifacts |
| Branch protection／CODEOWNERS／access review | 治理 | GitHub settings 與定期審查紀錄 |
| Frontend CI 設為 required check | SDLC 治理 | workflow 已執行 `pnpm test -- --run`；仍需 repository settings 啟用 required check |
| 第三方滲透／威脅模型 | 獨立保證 | 報告、修復 SLA、複測 |
| pnpm Low 1 | Low／追蹤 | 升級、上游修復或有期限接受紀錄 |

## 驗證機制說明

### 為何 158/158 可信度高於舊 CI 綠燈

舊 runner 組合可能沒有真正 discover xUnit v3 tests；命令仍以成功結束。現在同時使用 MTP-compatible runner、`--project` 與最低 150 tests，因此「0 tests 綠燈」會直接失敗。158 是實際執行案例數，與原始碼中的 `[Fact]`／`[Theory]` attribute 數不同。

### Coverage 的正確解讀

CI 已建立 Cobertura artifact，但目前 `150` 是 test-count floor，不是 line coverage 門檻。Coverage 報表可能包含第三方 source；下一階段應先產生 project-only baseline，再設定不鼓勵無效測試的合理門檻。

### 供應鏈的正確解讀

Audit 的 advisory 數不等於可利用漏洞數；但 High／Critical 直接依賴不能用「目前可能不可達」取代修補。現在的策略是升級、鎖檔、CI audit、CodeQL、Trivy 與 SBOM 多層交叉，而不是依賴單一 scanner。

## 來源與時效

- [Next.js Security](https://nextjs.org/blog)－Next.js 官方安全公告。
- [.NET 10 Download](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)－SDK／runtime security patch。
- [MongoDB Support Policy](https://www.mongodb.com/legal/support-policy/lifecycles)－MongoDB lifecycle。
- Dependency audit 應以每次 release 當下的 NuGet／npm registry 結果為準。

## 最終聲明

最準確的說法是：**架構、安全、測試與供應鏈的程式碼基線已形成企業級閉環；目前沒有 open High／Critical code blocker，但仍須正式環境與治理證據，才能取得生產上線認證。**

延伸閱讀：[風險台帳](./03-安全風險與修復台帳.md) · [需求與 Skills](./06-對話需求驗收與Skills紀錄.md) · [Open Design](./presentation/OPEN-DESIGN.md) · [文件中心](./README.md)
