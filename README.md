# 許氏入口網 · Hsu Family Portal

給家人使用的私人入口網：國農曆行事曆、跨年拜拜規則、成員、歷年行程與私人照片。新部署採 React＋Vite 和 Cloudflare Workers／Access／D1／私人 R2；尚須隔離雲端驗收，舊 Next.js／Supabase 路徑保留。公開的 [GitHub Pages 展示](https://helloapple080.github.io/HsuFamily/) 是唯讀假資料，沒有登入或私人 API。

- 開發、部署與維護：[Markdown](docs/guides/developer.md) · [HTML](docs/guides/developer.html)
- 家人操作：[Markdown](docs/guides/user.md) · [HTML](docs/guides/user.html)
- [MongoDB 離線轉換](docs/superpowers/notes/2026-11-12-legacy-converter.md) · [文件與歷史稽核](docs/README.md)

## 本機啟動

需 Node.js 22、pnpm 10.33。Cloudflare 本機模式不需要雲端帳號，也不會接受任何測試帳密或登入繞過。

```sh
cd frontend
pnpm install --frozen-lockfile --ignore-scripts
pnpm test
pnpm check:cloudflare
pnpm exec wrangler d1 migrations apply DB --local
pnpm dev:cloudflare
```

無 Access 設定時本機私人 API 回傳 503，登入請用[隔離雲端驗收](docs/guides/developer.md)流程。回退版本驗證使用 `pnpm lint`、`pnpm build`；公開展示使用 `pnpm build:pages && pnpm test:pages`。

舊 .NET／MongoDB 執行環境已從工作分支移除，程式可從 [Git 歷史](https://github.com/helloapple080/HsuFamily/commit/5e15500) 取回。現有部署及正式資料不受這次程式移除影響；上線仍須完成隔離雲端驗收、備份和還原。

[MIT License](LICENSE)
