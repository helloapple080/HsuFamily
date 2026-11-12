# Hosted Worship Preview Implementation Plan

**Goal:** 串接既有登入與 Supabase 農曆拜拜，讓核准家人可讀、管理員可維護。

**Architecture:** opt-in Next.js 同源 API，以伺服器 HttpOnly Cookie 維持 Supabase 會話。Route Handler 檢查來源、身分、角色及輸入；資料仍由 RLS 保護。沿用行事曆路徑、版型和 React Query。

- [x] 以 `frontend/src/lib/hosted/__tests__/portal.test.ts` 驗證未登入、未核准、跨站寫入、普通家人寫入與管理員日期流程。
- [x] 實作 `lib/hosted/portal.ts` 的 HTTP 邏輯及 `lib/hosted/supabase.ts` 的每請求資料存取；Next Route Handler 不在展示版建置。
- [x] 啟用登入、拜拜 CRUD 和年份查看；精簡版不連 SignalR，也不在導覽暴露尚未接線的功能。
- [x] 99 項前端測試、lint、CSP、三種建置及公開產物檢查通過；更新開發者及使用者的 Markdown 與 HTML 手冊。
- [ ] 雲端驗收使用獨立測試專案與假資料；補齊成員、行程、私人照片及資料遷移驗收前，不切換正式站。

## 安全審查與處置

- 獨立審查發現遠端登出故障時，本機 Cookie 原本會被保留；回歸測試先重現 503，再確認裝置登出、Cookie 清除及遠端撤銷未確認的提示。HTTPS 使用 `__Host-` Cookie，避免子網域植入同名 Cookie。
- 前端依賴掃描原回報 36 條公告（5 條重大、19 條高風險）。升級 Next.js 16.4.6、Axios 1.21.1、Zod 4.6.7 及受影響的間接依賴；`pnpm audit:prod` 重新掃描未報已知漏洞。公告數不等同應用程式可被利用的漏洞數。
- Next.js 獨立程序的 HTTP 檢查驗證匿名 401、跨站登入 403、私人 API 不快取、未接入頁面重新導向。資料庫使用嵌入式 PostgreSQL 驗證；SDK 登入使用模擬服務。沒有連接正式資料庫，也沒有部署雲端預覽。
