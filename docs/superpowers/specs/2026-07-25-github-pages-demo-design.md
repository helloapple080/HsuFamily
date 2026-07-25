---
title: GitHub Pages 公開展示模式設計
tags: [HsuFamily, GitHub-Pages, demo, static-export]
updated: 2026-07-25
status: approved-design
---

# GitHub Pages 公開展示模式設計

## 目標

為許氏入口網建立免費、可公開瀏覽的 GitHub Pages 展示版本。訪客不需登入即可查看日曆、家族成員與行程的完整介面；展示版不得連接正式 API、MongoDB 或 SignalR，也不得包含真實個資、帳號或秘密。

## 成功條件

- 網站由 GitHub Pages 透過 `https://helloapple080.github.io/HsuFamily/` 提供。
- `main` 更新後由 GitHub Actions 自動測試、靜態建置與部署。
- 日曆、成員與行程頁可直接導覽，並使用明確標示的虛構資料。
- 所有可能改變資料的操作均停用或顯示「展示模式不會儲存變更」。
- 正式部署仍使用既有動態 Next.js、.NET API、MongoDB 與 SignalR，不因展示模式而降低安全設定。

## 採用方案

採用「同一套前端、build-time 切換唯讀 Demo adapter」。相較於另建展示網站，此方案可重用現有 UI，避免兩套介面漂移；相較於瀏覽器攔截任意 API，明確的資料邊界更容易測試與維護。

未採用：

- 保留固定 Demo 帳密：會讓公開訪客誤以為是真實登入，也增加不必要的 session 模擬。
- 將完整系統硬改成 static-only：會破壞正式部署能力。
- GitHub Pages 直接呼叫正式 API：會引入 CORS、Cookie、資料權限與個資外洩風險。

## 架構

### 模式選擇

- `NEXT_PUBLIC_DEMO_MODE=true`：啟用公開展示模式。
- 一般正式建置未設定此值時，維持既有登入、API、Cookie、CSRF 與 SignalR 流程。
- `next.config.ts` 在 Demo build 使用 `output: "export"`、repository `basePath` 與相容的 asset prefix；正式版仍使用 `output: "standalone"`。

### 展示資料

- 在前端建立 typed demo fixtures，涵蓋日曆事件、成員及行程。
- 所有姓名、日期、電話、地址與備註均為虛構資料，不由 MongoDB 匯出。
- Demo data adapter 提供與頁面所需資料形狀一致的 read-only 查詢結果。

### 導覽與授權

- Demo 網址根路徑直接導向展示入口，不顯示登入表單。
- Portal shell 顯示持續可見的「公開展示模式」標記與不保存資料說明。
- Admin-only 區塊不得因 Demo 模式取得真實管理員權限；若需展示管理介面，只呈現不可送出的視覺狀態。

### 寫入與即時功能

- 新增、修改、刪除與登出等動作不得送出 HTTP request。
- UI 可以開啟表單供體驗，但送出時只顯示 Demo 提示並保持 fixtures 不變。
- SignalR 在 Demo 模式完全不建立連線，連線狀態改顯示為「展示資料」。

## 靜態路由與資產

- 所有展示路由都必須能在 `next export` 階段產生。
- GitHub Pages project path 固定為 `/HsuFamily`；內部導覽使用 Next.js routing 並尊重 `basePath`。
- 圖片使用靜態相容設定，不依賴 Next.js Image Optimization server。
- 產物輸出至 `frontend/out`，由 GitHub Pages Actions artifact 發布。

## GitHub Actions

新增獨立 Pages workflow：

1. Checkout。
2. 安裝 Node／pnpm，執行 frozen install。
3. 執行前端 test、lint 與 Demo static build。
4. 上傳 `frontend/out` Pages artifact。
5. 僅由 `main` 部署到 GitHub Pages；PR 只驗證、不發布。

Workflow 使用最小權限：build job 只讀 repository；deploy job 僅取得 `pages: write` 與 `id-token: write`。

## 錯誤處理

- Fixtures 載入失敗時使用既有 ErrorState，提供重新整理操作。
- Demo 模式若偵測到 API 或 SignalR 呼叫，測試必須失敗，避免公開展示誤連正式服務。
- 無 JavaScript 或未知路由時提供靜態可讀的 fallback／404 說明。

## 測試與驗收

- 單元測試：模式判定、fixtures、read-only 寫入阻擋、SignalR 不連線。
- 元件測試：Demo banner、直接導覽、三個主要頁面與寫入提示。
- 回歸：既有 54 項前端測試全部通過。
- 品質：ESLint、TypeScript、`next build`／static export 通過。
- 瀏覽器：桌面與手機 viewport 檢查首頁、日曆、成員、行程；確認沒有 API request、404、資產路徑錯誤或 console error。
- 遠端：GitHub Pages workflow 綠燈，公開 URL 回傳 HTTPS 200。

## 安全與隱私邊界

- 禁止把 `.env`、connection string、JWT secret、MongoDB URI 或真實使用者資料放入 Pages artifact。
- Demo fixtures 僅含明確虛構內容。
- GitHub Pages 不處理密碼或敏感交易。
- 公開頁面與文件必須說明這是唯讀展示，不是正式生產服務。

## 不在本次範圍

- 部署 .NET API、MongoDB 或 SignalR。
- 正式登入、跨裝置同步或永久保存。
- 自訂網域與付費雲端資源。
- 將既有 MongoDB 資料匿名化後公開。

