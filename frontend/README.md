# 許氏入口網 · Next.js 應用程式

使用 Next.js App Router、TypeScript、React Query 與 Supabase。預設以同源 `/api/hosted` 提供私人入口網；公開展示是另一種獨立的唯讀建置。

- `src/app`：登入、私人頁面、同源 API 路由。
- `src/components/features`、`src/hooks`：介面、表單和查詢。
- `src/lib/hosted`：伺服器端登入、授權、資料契約、存取及照片驗證。
- `scripts/migration`：只在本機執行的舊 MongoDB 匯出轉換，不屬於網站執行路徑。

Node.js 22 和 pnpm 10.33。將 `.env.example` 複製為 `.env.local`，依 [開發與部署手冊](../docs/guides/developer.md) 設定測試環境。

```sh
pnpm install --frozen-lockfile --ignore-scripts
pnpm dev
pnpm test
pnpm lint
pnpm build
pnpm build:pages && pnpm test:pages
```
