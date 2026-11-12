# Retire Legacy Runtime Implementation Plan

**Goal:** 移除工作分支的舊版執行環境，私人 Next.js／Supabase 入口網成為預設。

**Architecture:** 保留同源 API、資料庫政策、公開唯讀展示與離線搬遷。移除 .NET、MongoDB 執行設定、SignalR 和舊登入流程；不接觸現有部署或任何資料庫。

1. 用 `frontend/src/lib/hosted/__tests__/session.test.ts` 驗證未設定模式時預設啟用私人路由，展示模式仍拒絕；執行同一檔案先紅再綠。
2. 整理 `frontend/next.config.ts`、`frontend/src/lib/api.ts`、頁面及 hooks，直接使用同源 `/api/hosted`；保留正式模式的快取、登入及授權檢查。
3. 移除舊 `backend/`、Docker Compose／Caddy、SignalR、舊 Google Drive UI 與相關 CI，更新 `README.md`、`AGENTS.md` 和 `docs/guides/`；歷史稽核文件保留。原始程式可從 `5e15500` 取回。
4. 執行前端完整測試、ESLint、CSP、依賴掃描、預設正式建置及 GitHub Pages 靜態產物檢查；只推工作分支。

## 本機驗收結果

- 108 項測試通過，ESLint、CSP 和正式依賴掃描通過。
- 實際啟動正式建置，未設定雲端時私人 API 回 503，舊 API 回 404。
- 公開展示在本機存在 Next.js 開發快取時仍可建置；靜態產物不含登入和私人 API。
- 歷史程式與測試仍可從 Git 取回。離線搬遷工具、四份資料庫遷移、外部資料與既有部署未變更。
