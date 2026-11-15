# Cloudflare React＋Vite Implementation Plan

> **For agentic workers:** Use isolated ownership and test-driven checkpoints; verify the complete build before any deployment.

**Goal:** 保留現有 React 功能並交付 Cloudflare 可驗收的私人入口網，不切換現有環境或搬動正式資料。

**Architecture:** `frontend/spa` 用 Vite 組裝現有 React 功能；`frontend/edge` 的 Worker 負責同源 `/api/hosted`、Access 驗簽、D1 角色及儲存。Next.js／Supabase 和 GitHub Pages 保持原有入口及指令，當作回退與公開展示。

**Tech Stack:** React、TypeScript strict、Vite、React Query、Zod、lunar-javascript、Workers、Access、D1、R2、Images、Vitest。

## Global Constraints

遵守 [已確認的架構規格](../specs/2026-11-16-cloudflare-portal-design.md)。不開放註冊、不信任瀏覽器角色，不暴露私人圖片或密鑰，不將真實資料放進測試。使用免費層，不新增自架主機。顯示過期資料的行為或框架包裝不能削弱撤權。

- [x] `frontend/spa`：以 SPA 重用行事曆、成員、行程、照片及主題，不把 Next.js 或 Supabase Server API 打進前端。`GET /api/hosted/auth/me` 維持 `UserInfo`；Access 操作不需本站密碼。API 請求留在 hooks 及共用 client。
- [x] `frontend/edge/access.ts`、`http.ts` 與 `index.ts`：固定 origin，驗證 Access 簽章、issuer、audience、時效，查 `portal_access`，避免角色繞過。本機驗證未登入、偽造、撤權、跨來源寫入、不支援路徑、`no-store` 及正常角色。
- [x] `frontend/edge/records.ts`、`migrations/0001_portal.sql`：D1 schema 和 CRUD 存成員、行程、活動、拜拜規則。保留 JSON 結構與農曆展開，用版本拒絕舊寫入；本機驗證唯讀可見性、日期、搜尋、分頁、衝突。
- [x] `frontend/edge/photos.ts`、`migrations/0002_photos.sql`：R2 私人檔案、Images binding 僅於上傳轉檔。以 SQLite、圖片測試檔與模擬 binding 驗證所有者、假 MIME、尺寸、私人下載、EXIF、版本衝突和失敗補償，不輸出外部圖片網址。
- [x] Wrangler 隔離設定、靜態標頭、CI 檢查、Cloudflare 開發者／家人 Markdown 和 HTML；本機兩個 D1 遷移、SPA 建置與 Wrangler dry-run。
- [ ] 取得 Cloudflare 權限後才用假資料完成真實 Access、D1／R2、Images、免費層 CPU 與還原驗收。驗收前不匯入正式資料或切換正式站。
