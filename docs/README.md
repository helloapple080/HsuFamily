---
title: 許氏入口網文件中心
aliases: [HsuFamily 文件中心, 許氏入口網知識庫]
tags: [HsuFamily, documentation, Obsidian, governance]
updated: 2026-07-24
code_baseline: 57d1110
---

# 許氏入口網文件中心

## 基準結論

截至程式碼基準 `57d1110`，本專案已具備**企業級工程思維**，且已知的程式碼 Critical／High 安全基線已完成修復與回歸驗證。這代表架構、安全、測試、供應鏈與部署設定已有可持續執行的閉環；不代表已取得正式生產環境、法遵或第三方資安認證。

目前沒有仍開啟的程式碼 High／Critical release blocker。剩餘項目主要是正式 TLS／DNS／proxy、Secret Manager、監控告警、備份還原、RPO／RTO、事故演練、權限治理與獨立滲透測試等**環境及治理風險**。

## 驗證快照

| 項目 | 結果 |
|---|---|
| 本機 .NET | SDK `10.0.302`；ASP.NET Core／.NET runtime `10.0.10` |
| 後端測試 | `158/158` 通過；CI 設定最低預期測試數 `150` |
| 前端測試 | `54/54` 通過（12 個 test files） |
| 前端品質 | ESLint 通過；Next.js production build 通過 |
| NuGet | 5 個 projects 未發現已知 vulnerable package |
| pnpm production audit | Critical `0`、High `0`；剩餘 Low `1` |
| 供應鏈 | CodeQL、Trivy High/Critical gate、CycloneDX SBOM、Dependabot 已納入 repository |
| 遠端證據 | [CI run 30112837936](https://github.com/helloapple080/HsuFamily/actions/runs/30112837936) 與 [CodeQL run 30112838098](https://github.com/helloapple080/HsuFamily/actions/runs/30112838098) 全數通過 |

> 測試通過與依賴掃描是程式碼證據；正式上線仍需完成 [部署與維運手冊](./04-部署與維運手冊.md) 的環境驗收。

## 閱讀順序

1. [管理摘要](./01-管理摘要.md)－供長官快速決策。
2. [系統與架構全覽](./02-系統與架構全覽.md)－技術與信任邊界。
3. [安全風險與修復台帳](./03-安全風險與修復台帳.md)－已解決與剩餘風險分流。
4. [部署與維運手冊](./04-部署與維運手冊.md)－部署、驗證、備份與事故處理。
5. [企業級成熟度與後續路線圖](./05-企業級成熟度與後續路線圖.md)－管理成熟度與投資順序。
6. [對話需求驗收與 Skills 紀錄](./06-對話需求驗收與Skills紀錄.md)－完整需求、流程與工具使用證據。
7. [Skills 全面自檢報告](./07-Skills全面自檢報告.md)－截至本基準的技術自檢結論。
8. [簡報 Open Design 規格](./presentation/OPEN-DESIGN.md)－10 頁長官簡報的最新敘事來源。

簡報檔：[HsuFamily-Enterprise-Readiness.pptx](./presentation/HsuFamily-Enterprise-Readiness.pptx)。PPTX 已依本基準重新產製，並通過逐頁渲染、overflow 與 Open Design template fidelity 驗證。

## 文件治理

- Markdown 是稽核與 Obsidian 的 source of truth；PPTX 只做管理摘要。
- 每份主文件都包含 YAML frontmatter、基準 SHA 與相對連結，能直接匯入 Obsidian。
- 本機 Obsidian vault 會在最終提交後同步，並以檔案雜湊比對留證。
- 歷史設計與執行計畫保留在 `docs/superpowers/`，它們記錄當時決策，不代表當前 open risk。
- 安全不是一次性證書；每次 release 應重跑測試、依賴掃描、CodeQL／Trivy，並更新風險台帳。

## 證據優先順序

1. 實際命令輸出與 CI artifacts。
2. 程式碼與測試。
3. 本文件台帳。
4. 簡報摘要。

若四者不一致，以較新的可重現證據為準。
