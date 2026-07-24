---
title: 許氏入口網企業級工程簡報 Open Design
aliases: [HsuFamily Open Design, 長官簡報規格]
tags: [HsuFamily, presentation, open-design, governance]
updated: 2026-07-24
code_baseline: 57d1110
pptx_sync: complete
---

# 簡報 Open Design 規格

## 文件狀態

本檔是 `57d1110` 的最新簡報敘事 source of truth。[HsuFamily-Enterprise-Readiness.pptx](./HsuFamily-Enterprise-Readiness.pptx) 為 10 頁可編輯簡報，已依本基準重製並通過逐頁渲染、overflow 與 template fidelity 驗證；內容仍明確標示尚未取得正式生產認證。

簡報以 GitHub Primer 的開源產品語彙為靈感，隨專案採 MIT License，可編輯與再利用。

## 核心管理訊息

> 許氏入口網已具備企業級工程思維，程式碼安全基線已閉環；下一階段不是重寫，而是完成正式環境與治理認證。

所有頁面必須維持兩條邊界：

- **已完成：** 程式碼、測試、依賴、CI 與 deployment template 的可驗證控制。
- **待驗收：** 正式 TLS、secrets、backup restore、observability、governance、E2E 與獨立 security assessment。

## 設計目標

- 長官可在 10 分鐘內理解「修了什麼、證據是什麼、還要核准什麼」。
- 每頁只有一個 management takeaway；技術名詞旁附管理意義。
- 數字只使用可追溯證據：Backend 158、Frontend 54、NuGet 5 projects clean、pnpm High/Critical 0／Low 1。
- PowerPoint 保留原生文字、表格與圖形，不把關鍵內容烘焙成不可修改圖片。
- 不使用「零風險」、「完全安全」、「企業認證完成」等過度宣稱。

## 視覺系統

| Token | 色碼 | 用途 |
|---|---|---|
| Ink | `#0D1117` | 主文字／封面 |
| Canvas | `#FFFFFF` | 背景 |
| Soft | `#F6F8FA` | 證據／資訊區塊 |
| Line | `#D0D7DE` | 分隔線 |
| Green | `#1F883D` | 已完成／verified |
| Blue | `#0969DA` | 架構／測試證據 |
| Amber | `#BF8700` | 正式環境待驗收 |
| Red | `#CF222E` | 歷史高風險；只用於 before 狀態 |

字體優先 PingFang TC；16:9；標題至少 40–48 px，內文至少 18–22 px。動效只用於揭露順序，遵循 Emil 的克制與 reduced-motion 思維。

## 10 頁敘事結構

| 頁 | 標題 | 唯一管理訊息 | 建議視覺 |
|---:|---|---|---|
| 1 | 許氏入口網企業級工程治理成果 | 已有企業級基線，正式環境認證待完成 | 封面＋雙狀態標籤 |
| 2 | 為何要做這次全面修復 | 從家庭作品提升為可治理、可驗證的正式專案 | Before／After 對照 |
| 3 | 風險已如何下降 | 程式碼 open Critical／High 由歷史風險收斂為 0 | 風險漏斗；環境風險另列 |
| 4 | 架構不再靠 Controller 撐住 | 業務進 Application Services，guard 防止回歸 | Clean Architecture flow |
| 5 | 登入、資料與即時連線安全 | Cookie／CSRF／rotation／Mongo 最小權限／SignalR auth 形成縱深 | 信任邊界圖 |
| 6 | 供應鏈與部署預設安全 | Next／Axios／.NET／NuGet 已修補；prod 只由 Caddy TLS 對外 | Dev／Prod topology |
| 7 | 綠燈是真的跑過 | xUnit MTP 假綠已關閉；158＋54、floor 150、coverage artifact | Evidence scorecard |
| 8 | 使用者看得到的品質提升 | Admin gating、a11y、Taiwan calendar、error recovery、shared SignalR | 5 張 outcome cards |
| 9 | 還有哪些風險 | 剩餘是 TLS 實測、secrets、backup、monitoring、governance、E2E | Owner／evidence table |
| 10 | 長官要核准什麼 | 核准正式環境驗證與治理投入，不重寫技術棧 | 3-step decision path |

## 必須出現的證據

| 證據 | 表述 |
|---|---|
| 程式碼基準 | `57d1110` |
| Backend | `158/158`；CI minimum expected tests `150` |
| Frontend | `54/54`；ESLint／Next production build PASS |
| Runtime | SDK `10.0.302`；ASP.NET Core／.NET `10.0.10` |
| NuGet | 5 projects，no known vulnerabilities |
| pnpm | Critical 0、High 0、Low 1 |
| Supply chain | CodeQL、Trivy High/Critical、CycloneDX SBOM、Dependabot |
| 尚未完成 | 正式環境與治理認證 |

## Skills 對設計的影響

- `presentations`：可編輯 PPTX、render、montage、overflow QA。
- Impeccable／ui-ux-pro-max／ui-styling：資訊層級、可及性、狀態語意與一致元件。
- Vercel web guidelines／React practices：將技術證據轉成清楚且不誤導的產品敘事。
- Emil design engineering：克制配色、動效與細節，避免「資安簡報＝滿頁警示紅色」。

Anthropic `frontend-design` 與 `taste-skill` 未安裝、未使用；不得列入簡報製作 credit。

## 更新與 QA 流程

1. 先以本 Open Design 更新 10 頁 PPTX。
2. Render 每頁為圖片，檢查字級、截斷、重疊、色彩與繁中換行。
3. 以 montage 檢查整體節奏；每頁只留一個 takeaway。
4. 搜尋並移除所有過期的 blocker、舊 dependency version 與「本機缺少 .NET」敘事。
5. 確認第 9～10 頁保留正式環境與治理風險，不能把修復說成零風險。
6. PPTX 與 Markdown 同步到 Obsidian 後比對雜湊，再將 `pptx_sync` 改為 `complete`。

返回：[文件中心](../README.md) · [管理摘要](../01-管理摘要.md) · [風險台帳](../03-安全風險與修復台帳.md) · [Skills 紀錄](../06-對話需求驗收與Skills紀錄.md)
