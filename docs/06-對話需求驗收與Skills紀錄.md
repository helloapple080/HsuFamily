---
title: 對話需求驗收與 Skills 紀錄
tags: [HsuFamily, acceptance, skills, audit]
updated: 2026-07-24
---

# 對話需求驗收與 Skills 紀錄

> **後續驗收更新（2026-07-24）：** 本文件記錄前一階段交付。最新 `pnpm audit` 與 Playwright 自檢已重新發現供應鏈、部署、無障礙與農曆文化風險；目前狀態請以 [Skills 全面自檢報告](./07-Skills全面自檢報告.md) 為準。

## 驗收結論

本次對話的核心交付已完成：現代化／安全評估、Critical／High 修復、其餘可由程式碼處理的風險、GitHub 推送、結構化文件、長官簡報、MIT 開源授權與本機 Obsidian vault。

但「所有風險都已消失」與「一開始就使用最新熱門外部 skills」不成立：正式 TLS、Secret Manager、監控、備援、完整瀏覽器 E2E、CSP nonce 與組織治理仍需環境／制度投入；Superpowers 與 UI/UX Pro Max 是在本次驗收回合才補做查證、安裝與使用紀錄。

## 需求逐項對帳

| 使用者要求 | 實際成果／證據 | 判定 |
|---|---|---|
| 依 2026 前後端趨勢評估專案 | 確認 .NET 10、Next.js 16 App Router、TS strict、React Query、Zod、MongoDB、SignalR 適配專案 | 已達成 |
| 判斷是否具備企業級安全 | 建立安全台帳與成熟度邊界：具企業級工程思維，但生產認證需環境驗收 | 已達成，且有條件 |
| 自動推送 GitHub | 所有修復與文件均推送 `main`；CI run `30028005532` 全綠 | 已達成 |
| 修復 Critical／High | HttpOnly Cookie、CSRF、refresh hash/rotation/replay、SignalR auth、Mongo 最小權限、安全測試 | 已達成 |
| 修正其餘部分 | refresh 過期 bug、多分頁競態、forwarded headers、Data Protection、非 root、CSP、錯誤／日誌、版本固定、既有 volume migration | 程式碼範圍已達成 |
| 完整文件 | `docs/01` 至 `docs/06`、風險台帳、維運手冊與路線圖 | 已達成 |
| 長官看得懂的 Open Design 簡報 | 10 頁可編輯 PPTX，已渲染、逐頁檢查及 overflow test | 已達成 |
| 本機 Obsidian | `/Volumes/USB/阿豬程式資料/HsuFamily-Obsidian` | 已達成 |
| GitHub 開源 | 加入 MIT License、文件中心、Open Design 規格與 PPTX | 已達成 |
| 尋找並下載最新實用 skills | 前期只有既有官方／專案 skills；2026-07-24 才補查並安裝 Superpowers v6.1.1、UI/UX Pro Max v2.11.0 | 現在已補達成；前期未達成 |

## 對話工作流程重建

1. 盤點技術棧、架構、驗證與安全邊界。
2. 修復最高風險認證與資料庫問題。
3. Git push 後以 GitHub Actions 驗證 .NET build/test 與 Next.js lint/build。
4. 再盤點剩餘風險，修正 refresh 邏輯、容器、proxy、key persistence、CSP、日誌與 migrations。
5. 新增 API security integration tests，反覆修正至 CI 全綠。
6. 建立管理摘要、架構、安全、維運、成熟度文件及 10 頁簡報。
7. 建立 MIT 授權與 Obsidian vault，推送並再次驗證 CI。
8. 本回合追溯驗收，承認外部熱門 skill 搜尋／安裝原先沒有證據，立即補做。

## 實際曾用於前期作品的 Skills

| Skill | 用途 |
|---|---|
| `aspnet-core` | ASP.NET Core pipeline、認證、授權、測試與部署模式 |
| `security-best-practices` | TypeScript／JavaScript 安全檢查 |
| `error-handling` | API exception middleware 與前端錯誤狀態 |
| `mongodb-query` | MongoDB 索引、查詢與最小權限 |
| `playwright` | 瀏覽器驗證策略；完整 E2E 尚待可啟動環境 |
| `code-review` | 變更自我審查與 CI 前檢查 |
| `presentations` | PPTX 建立、render、montage、overflow QA |

## 2026-07-24 新增安裝

### Superpowers

- 來源：[obra/superpowers](https://github.com/obra/superpowers)
- 固定版本：`v6.1.1`（2026-07-02 release）。
- 安裝位置：`~/.codex/skills/`。
- 安裝內容：brainstorming、TDD、systematic debugging、planning、code review、verification、worktrees、parallel／subagent workflows 等 14 個 skills。
- 本回合實際使用：`using-superpowers` 與 `verification-before-completion` 進行需求追溯與證據核對。

### UI/UX Pro Max

- 來源：[nextlevelbuilder/ui-ux-pro-max-skill](https://github.com/nextlevelbuilder/ui-ux-pro-max-skill)
- 查證時 GitHub 約 109k stars，最新 release `v2.11.0`（2026-07-13），且 2026-07-23 仍有更新。
- 安裝方式：官方 `ui-ux-pro-max-cli@2.11.0`，`--ai codex --global --offline`。
- 安裝位置：`~/.codex/skills/ui-ux-pro-max/`。
- 本回合實際使用：產生 Hsu Family Portal design-system 建議，並查詢 accessibility、loading、forms、navigation 與 Next.js 規則。

查無可信且可驗證、名稱精確為「OpenDesign UIUX Designer」的 Codex skill，因此沒有安裝同名不明來源；改採活躍度、社群採用與 Codex 支援皆可驗證的 UI/UX Pro Max。

## UI/UX Pro Max 補驗收結論

推薦方向為 `Accessible & Ethical`：高對比、16px 以上內文、清楚 focus、鍵盤操作、reduced motion、44×44px touch targets。現有專案已使用 App Router、Lucide SVG icons、loading skeleton、shadcn primitives 與 responsive layout，方向一致。

尚應另開 UI/UX 強化任務逐頁驗證：

- 375／768／1024／1440 px 與 landscape 實機版面。
- skip link、route change focus 與完整鍵盤操作。
- 所有 icon-only controls 的 accessible name。
- 觸控目標至少 44×44 px、錯誤欄位 focus 與 aria-live。
- `prefers-reduced-motion`、light/dark contrast 與 Loading／empty／error recovery。

以上是新 skill 找出的 UI 驗收範圍；尚未因「安裝 skill」而自動變成已完成。

## 仍未完成且不能假稱完成

- 正式 TLS、HSTS、實際 proxy IP／hosts／origins。
- Secret Manager、秘密輪替與緊急撤銷流程。
- 集中 logs、metrics、tracing、alerts 與值班制度。
- MongoDB 備份還原、RPO／RTO、災難演練。
- 真實 browser login → refresh → logout Playwright E2E。
- AuthController 業務邏輯移入 Application Service。
- CSP nonce、CodeQL／SBOM／container scan、branch protection／CODEOWNERS。
- UI/UX Pro Max 所列跨裝置、無障礙與互動完整驗收。

## Codex 生效方式

安裝完成後需重啟 Codex。重啟後，新任務會由 Codex 自動發現 `~/.codex/skills` 中的 Superpowers 與 UI/UX Pro Max；專案既有 `.agents/skills` 仍保留作為技術棧專用規範。
