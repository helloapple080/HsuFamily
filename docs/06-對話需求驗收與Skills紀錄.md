---
title: 對話需求驗收與 Skills 紀錄
aliases: [HsuFamily 需求驗收, Skills 使用紀錄]
tags: [HsuFamily, acceptance, skills, audit, conversation]
updated: 2026-07-24
code_baseline: a4ab891
---

# 對話需求驗收與 Skills 紀錄

## 驗收結論

本次對話要求的核心程式工作已達成：2026 技術適配評估、Critical／High 修復、其餘可由 repository 處理的風險、企業級文件、Open Design 敘事及 Git 工作流程均已建立。最終判定是：

- **具備企業級工程思維。**
- **程式碼安全基線已閉環，目前無 open High／Critical code blocker。**
- **尚未取得正式生產環境認證，不能聲稱所有風險消失。**
- 工作基準為 worktree branch 的 `a4ab891`；文件與簡報完成後將以最終提交及遠端 CI run 留證。

## 使用者需求逐項對帳

| 使用者要求 | 實際交付 | 判定／邊界 |
|---|---|---|
| 以 2026 最新前後端趨勢評估 | .NET 10、Next.js 16 App Router、TS strict、React Query、Zod、MongoDB 7、SignalR 的適配與現代寫法盤點 | 已達成 |
| 判斷是否具企業級安全 | 建立明確成熟度定義、風險台帳與正式環境邊界 | 已達成；不是認證 |
| 先修 Critical／High | Cookie／CSRF／refresh／Mongo 權限／SignalR auth／供應鏈／production deployment | 已達成 |
| 再修正所有可處理部分 | 架構、validation、bounded queries、middleware、admin race、UI／a11y、農曆、realtime、測試假綠 | 已達成於 repository 範圍 |
| 保留既有 MongoDB 資料 | 未 drop／清空／刪除 volume；新限制只約束未來寫入並保留舊資料讀取相容 | 已達成 |
| 使用 worktree | 隔離於 `.worktrees/comprehensive-remediation`，branch `codex/comprehensive-remediation` | 已達成 |
| 可自動推 GitHub | 已建立 Conventional Commits 與 CI；本基準仍待主 agent 完成文件 commit、push／PR | 授權已取得；最終動作待完成 |
| 完整結構化 Markdown | 文件中心、01～07、Open Design、frontmatter、互鏈 | 本次已更新 |
| 長官可讀簡報 | 10 頁可編輯 PPTX 與 Open Design 規格 | 已依 `a4ab891` 同步並通過版面驗證 |
| 本機 Obsidian | 先前已建立 vault；Markdown 適合直接同步 | 本次版本待文件 commit 後重新同步／hash 驗證 |
| 使用最新且適合的 skills | 實際使用 Superpowers、後端／專案、UI／UX、React 與 presentation skills | 已達成並誠實列示未使用項 |
| 減少 token／不必要步驟 | 後段改為 worktree、明確 task brief、批次靜態 gate、只對 blocker 重審，不重複長篇報告 | 已採納 |

## 對話與執行流程重建

### 第一階段：盤點與最高風險

1. 使用者要求完善專案，並詢問 2026 技術趨勢與企業級安全。
2. 盤點認證、授權、MongoDB、部署、前端與 CI。
3. 先修 localStorage token、CSRF、refresh token 明文／重放、SignalR anonymous、Mongo root／port 等 Critical／High。
4. 使用 GitHub CLI／CI 建立可驗證流程，而不是只做靜態宣稱。

### 第二階段：全風險修復與資料保護

1. 使用者要求列出剩餘風險並「全部修復」。
2. 確認 MongoDB 已有資料後，採不破壞策略：不清資料、不刪 volume、不做 destructive migration。
3. 修正 proxy、Data Protection、容器 non-root、health checks、安全 headers、secrets fail-fast、既有 volume app user 初始化。
4. 將未能由程式碼解決的 TLS、Secret Manager、備份還原、監控與治理明確保留為 open。

### 第三階段：企業文件、簡報與 Skills 追溯

1. 建立管理摘要、架構、風險、Runbook、成熟度與驗收文件。
2. 建立 MIT 開源的 10 頁 Open Design PPTX 與 Obsidian vault。
3. 使用者要求追查是否真的使用熱門 skills；因此補做 skill inventory，拒絕把未安裝／未執行的名稱寫成已使用。

### 第四階段：worktree 全面 remediation

1. 建立隔離 worktree 與四份執行計畫：供應鏈／部署、後端、安全與架構、前端可靠性／a11y、驗證／文件／release。
2. 升級 Next／Axios／.NET patch，建立 production Caddy／Compose、安全 CSP 與 CI supply-chain gates。
3. 把 Auth／Event／Member／Itinerary 業務流程移入 Application Services，建立 architecture guards 與 validation limits。
4. 修正 refresh history、legacy data、query boundedness、middleware order、rate partition、1 MiB body limit 與 admin bootstrap race。
5. 修正 Admin UI、forms、error recovery、Taiwan calendar、reduced motion 與共享 SignalR lifecycle。

### 第五階段：驗證假綠修正

1. 發現 xUnit v3 在 runner／adapter 組合下可能 0 tests 仍成功。
2. 改用 Microsoft.Testing.Platform 與 `xunit.v3.mtp-v2`，CI 指定 `--project`。
3. 加入 `--minimum-expected-tests 150`、Cobertura artifact，並排除 AppleDouble sidecars。
4. 補 NuGet transitive CVE overrides 與可執行的 admin duplicate-key tests。
5. 最終證據：Backend `158/158`、Frontend `54/54`、lint／build 通過、NuGet 無已知漏洞、pnpm High／Critical 0。

## 風險與修復摘要

| 類別 | 主要問題 | 結果 |
|---|---|---|
| Auth／Session | token 暴露、CSRF、refresh replay／race | 已閉環 |
| Clean Architecture | Controller／Infrastructure 耦合 | 已閉環並有 guard |
| Input／DoS | 無界文字、集合、查詢、日期、request body | 已閉環 |
| Deployment | production HTTP／Cookie／proxy／CORS 不安全 | repository baseline 已閉環；正式環境待驗收 |
| Supply chain | Next／Axios／.NET／NuGet CVEs、缺掃描 | High／Critical 已閉環；Low 1 追蹤 |
| Frontend UX／a11y | Admin controls、forms、motion、error recovery | 已閉環並有 54 tests |
| Realtime | 多連線、StrictMode、重連漏資料 | shared lifecycle 已閉環 |
| Testing | xUnit MTP 假綠、無 test floor／artifact | 已閉環；158 tests／floor 150 |
| Operations | TLS 實測、secrets、backup、monitoring、governance | 仍 open，需環境／組織投入 |

完整逐項台帳見 [安全風險與修復台帳](./03-安全風險與修復台帳.md)。

## 實際使用的 Skills 與選擇理由

### 流程與驗證

| Skill／能力 | 實際用途 | 為何適合 |
|---|---|---|
| Superpowers | brainstorming、writing plans、worktrees、TDD、systematic debugging、subagent-driven work、code review、verification | 把大型修復拆成可驗證 task，避免一次改壞整個專案 |
| `verification-before-completion` | 在宣稱完成前重跑 tests／lint／build／audit | 防止「看起來有改」或 xUnit 0 tests 假綠 |
| `receiving-code-review` | 對 admin bootstrap、duplicate-key seam 等 reviewer blocker 做技術驗證後修正 | 不盲從，也不忽略可重現問題 |

### 後端與專案專用

| Skill／能力 | 實際用途 | 為何適合 |
|---|---|---|
| `aspnet-core` | .NET 10 pipeline、auth／authorization、CSRF、rate limit、HSTS、testing／operations | 使用官方 ASP.NET Core 現代基線 |
| 專案 `api-development` | Controllers／DTOs／Application ports／Repositories | 遵守本 repo 的 Clean Architecture 與繁中註解規則 |
| 專案 `mongodb-query` | indexes、bounded query、refresh history、legacy data | 保護既有 MongoDB 資料與文件大小 |
| 專案 `signalr-realtime` | Hub authorization、reconnect 與 query resync | 對應專案即時行事曆需求 |
| 專案 `error-handling` | API exception、trace ID、前端 retry／ErrorState | 建立安全且可恢復的失敗體驗 |
| 專案 `lunar-calendar` | 農曆格式、生日資料與台灣節日 | 避免文化與資料正確性回歸 |
| 專案 `nextjs-component`／`code-review` | App Router 元件規則、自我審查 | 維持 repo 技術棧與品質慣例 |

### 前端設計與簡報

| Skill／能力 | 實際用途 | 選擇理由 |
|---|---|---|
| `impeccable` | UI audit、資訊層級、狀態、forms、a11y、polish | 範圍完整，適合既有 portal 的系統性改善 |
| `web-design-guidelines` | 依 Vercel web interface guidelines 檢查語意、focus、forms、navigation | 提供可落地的 Web 規範 |
| `vercel-react-best-practices` | React／Next performance、state、render 與 bundle 檢查 | 與 Next.js 16 技術棧直接匹配 |
| `emil-design-eng` | 克制的動效、micro-interaction、reduced motion 與視覺細節 | 避免過度動畫，符合家族 portal 的穩定感 |
| `ui-ux-pro-max` | design system、a11y、44px touch、forms、responsive checklist | 資訊量大，適合全面 audit 與交叉核對 |
| `ui-styling` | shadcn／Tailwind／Radix 可及元件與 token 實作 | 與現有 UI stack 相容，不需重寫 |
| `presentations` | 既有 10 頁 PPTX 建立、render 與 overflow QA | 適合可編輯長官簡報；本次 md-only 更新未重製 PPTX |

## 明確未安裝／未使用

| 名稱 | 狀態 | 原因 |
|---|---|---|
| Anthropic `frontend-design` | 未安裝、未使用 | 當前 Codex skill catalog 無此可呼叫項；其 UI 設計範圍已由 Impeccable、ui-styling、Vercel guidelines 與 ui-ux-pro-max 涵蓋 |
| `taste-skill` | 未安裝、未使用 | 缺少本專案必要性與額外收益證據；現有設計 skills 已覆蓋視覺品味與 polish |
| 「OpenDesign UIUX Designer」 | 未以同名 skill 安裝 | Open Design 在本專案是簡報設計方法／規格，不冒充成已驗證外部 skill |

未安裝不代表品質較低；選擇原則是「與技術棧匹配、來源可驗證、能產生測試或可審查成果」，不是收集最多名稱。

## 文件、簡報與 Obsidian 狀態

- `docs/README.md` 與 01～07 已更新至 `a4ab891`。
- `docs/presentation/OPEN-DESIGN.md` 已提供新的 10 頁敘事。
- 現有 PPTX 仍是先前版本；在依新敘事重製前不得對外當作最新結論。
- 本機 Obsidian vault 先前存在，但本次 Markdown 尚待最終 commit 後同步與 hash comparison。

## 未完成且不能假稱完成

- 正式生產 TLS／proxy／Secret Manager／monitoring／backup restore／RPO／RTO。
- 正式環境 E2E、容量／災難演練與第三方滲透測試。
- GitHub branch protection／CODEOWNERS／權限定期複核的外部設定證據。
- 本文件變更的 commit、push、最終 CI run、PPTX 內容同步與 Obsidian hash 驗證。

延伸閱讀：[管理摘要](./01-管理摘要.md) · [Skills 自檢](./07-Skills全面自檢報告.md) · [Open Design](./presentation/OPEN-DESIGN.md) · [文件中心](./README.md)
