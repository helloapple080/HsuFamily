# 許氏入口網 — AI Agent 指令

## 共同目標

優先保全功能完整性、資訊安全、使用便捷、輕量與穩定。框架是工具，不是驗收條件。引進服務或依賴前，先說明維護成本、限制、資料可攜性與回復方式；現有簡單做法足夠時不要新增基礎設施。

## 執行模式

- 新私人部署走 React＋Vite、TypeScript 與 Cloudflare Workers、Access、D1、私人 R2。使用同源 `/api/hosted`，更新後重新查詢資料，修改與刪除使用版本防止互蓋。Next.js／Supabase 是保留的回退方案；尚未通過隔離雲端驗收前不得切換正式部署。
- 公開 GitHub Pages 展示版只使用虛構資料，唯讀且無登入；不得建置私人 API。
- 原版執行程式已退出工作分支，歷史稽核文件和離線搬遷工具保留。移除程式不代表已切換既有部署，也不授權刪除 MongoDB 或照片。

## 開發規則

- 前端遵守 `spa`、`app`、`components/features`、`hooks`、`lib`、`types` 邊界。Cloudflare 的 SPA 與 Next.js 入口共用 React 功能和契約，資料由 React Query 管理，不在元件直接 fetch。沿用既有 shadcn/ui，勿直接修改其基礎元件。
- 私人 API 將 HTTP／授權、領域驗證和資料存取分開，回應使用 `ApiResponse<T>`（照片二進位下載例外）。
- TypeScript 使用 strict，不使用 any，外部資料驗證後才使用。型別優先使用 interface，聯集可用 type。
- Cloudflare API 每次驗證 Access JWT 的簽章、issuer、audience 與身分，再查 D1 家人資格；管理員寫入還要驗證來源及角色。D1 沒有 PostgreSQL RLS，Worker 是私人資料邊界，SQL 使用參數化查詢，D1 和 R2 不對外公開。Next.js 回退須維持 PostgreSQL RLS，正式 API 不用 service-role 繞過權限。
- 不在前端暴露機密，不記錄密碼或憑證，不使用 dangerouslySetInnerHTML。驗證請求來源、內容、大小與日期；私人回應不得共用快取。照片上傳需驗證實際內容、限制大小和授權。
- 農曆使用 lunar-javascript；保留跨年重複、閏月與缺日規則。顯示用「農曆正月初一」、「農曆閏六月十二」。民用日期不因時區偏移而換日。
- 保留可還原的備份，先用假資料驗證登入、角色、資料匯入及還原；切換前保留既有部署及資料。開發者與家人手冊維護 Markdown 與 HTML，清楚標示哪些功能已驗收。
- UI、程式註解使用繁體中文；識別字使用英文。Git 使用英文 Conventional Commits。
