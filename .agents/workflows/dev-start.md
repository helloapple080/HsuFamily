---
description: 啟動 Next.js 與 Supabase 測試環境
---

# 啟動開發環境

使用 Node.js 22、pnpm 10.33 和隔離的 Supabase 測試專案。依 [部署手冊](../../docs/guides/developer.md) 套用遷移並設定 `frontend/.env.local`。

```sh
cd frontend
pnpm install --frozen-lockfile --ignore-scripts
pnpm dev
```

開啟 `http://localhost:3000`。已設定時，未登入的 `/api/hosted/auth/me` 應回 401；未設定時應回 503。不可連向正式資料庫進行開發。
