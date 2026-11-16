# 許氏入口網 Cloudflare 測試站：操作紀錄與零扣款檢查

2026-11-16（台北時間） · 測試程式版本 `a2c2761` · [開發者手冊](developer.md) · [家人手冊](user.md)

## 結論先看

已把 React＋Vite 的測試版本上傳到 Cloudflare，但沒有開啟對外網址。測試資料庫 D1 能備份並還原；目前只有一筆假拜拜規則和一個停用的示例帳號。正式網站、MongoDB 和既有照片未動。現在不用重做登入、建立資料庫，也不要為了讓測試站上線按「Upgrade」。

## 實際做了什麼

1. **準備程式。** 沿用 React 介面，改由 Vite 建置；Worker 分開管登入、權限與資料。保留 Next.js／Supabase 回退和 GitHub Pages 假資料展示。程式在 `codex/cloudflare-react`；[GitHub CI](https://github.com/helloapple080/HsuFamily/actions/runs/43775310531) 已通過。
2. **授權 CLI。** 我啟動 Wrangler，你在 Cloudflare 瀏覽器按允許。第一次交換憑證逾時；查到 IPv6 不通，IPv4 能連線，改用 IPv4 優先後重新授權成功。`wrangler whoami` 已確認登入；文件不公開信箱、權杖、帳戶編號或資料庫 UUID。
3. **檢查費用。** 後台 Workers plans 顯示 Free 是目前方案；R2 Overview 顯示已開通，但沒有桶，使用量為零。這次沒有升級、購買網域、建 R2 桶或開 Images；部署後再查看 R2，當期應計費金額仍顯示 US$0.00。
4. **驗證本機建置。** 用 `pnpm check:cloudflare` 做 Vite 建置和部署乾跑，再執行 `pnpm test`。148 項通過，含權限、農曆、照片驗證和競爭寫入。本機照片測試不等於雲端照片驗收。
5. **建立隔離資料庫。** 確認雲端沒有同名 D1 後建立 `hsu-family-staging`（亞太區）；兩個遷移 `0001_portal.sql`、`0002_photos.sql` 均成功。雖已有照片資料表，但沒有 R2 綁定或照片。
6. **上傳 Worker。** 用 Git 忽略的 `wrangler.staging.local.jsonc` 乾跑，確認只連測試 D1，再用相同設定部署。不開 `workers.dev`、預覽網址、網域或排程。Cloudflare 回報 `No targets deployed`：程式已上傳，沒有對外網址。
7. **假資料與還原。** 匯入假規則和停用帳號；補齊樣本標題、閏月規則和備註後重跑同一筆。匯出三張應用表，在空白的本機 SQLite 還原。完整性檢查為 `ok`、無外鍵錯誤，帳號／規則／照片筆數為 `1 / 1 / 0`，日期為農曆八月十五。這次沒有雲端照片可用來驗收圖片備份。

## 怎麼重做（維運者）

以下在專案的 `frontend` 資料夾操作；`--remote` 代表會改動或讀取雲端，不能用在正式站。這次已做過，只有換電腦、重建測試環境或演練才需要重跑。

```sh
# 若登入逾時，僅在此終端優先使用已驗證可連線的 IPv4。
export NODE_OPTIONS=--dns-result-order=ipv4first
pnpm exec wrangler whoami
# 若尚未登入，才執行 pnpm exec wrangler login 並手動授權。
pnpm check:cloudflare
pnpm test
pnpm exec wrangler d1 list --json
# 只有結果沒有 hsu-family-staging 才建立：
pnpm exec wrangler d1 create hsu-family-staging --location apac
```

從 `wrangler.jsonc` 複製為 `wrangler.staging.local.jsonc`，把 Worker 與 D1 名稱都換成 `hsu-family-staging`、D1 ID 換成剛核對的測試 UUID。保留 `workers_dev: false`、`preview_urls: false`；刪除 `r2_buckets` 和 `images`，也不加入 `vars`、公開路由或排程。先確認設定檔被 Git 忽略、乾跑只列出 `DB`，才做後續。

```sh
git check-ignore wrangler.staging.local.jsonc
pnpm exec wrangler deploy --dry-run --config wrangler.staging.local.jsonc
pnpm exec wrangler d1 migrations apply DB --remote --config wrangler.staging.local.jsonc
pnpm exec wrangler deploy --config wrangler.staging.local.jsonc
pnpm exec wrangler d1 execute DB --remote --config wrangler.staging.local.jsonc \
  --file ../docs/guides/examples/cloudflare-staging-seed.sql
```

備份與還原只用假資料。匯出時 Wrangler 可能顯示短期有效下載連結；不要貼到聊天、Git 或共用文件。若正式資料要備份，應使用加密儲存，並另行備份圖片本體；D1 的 SQL 不含圖片。

```sh
audit_dir=$(mktemp -d)
pnpm exec wrangler d1 export DB --remote --config wrangler.staging.local.jsonc \
  --output "$audit_dir/backup.sql" --table portal_access portal_records portal_photos
sqlite3 -bail "$audit_dir/restore.sqlite" < "$audit_dir/backup.sql"
sqlite3 -bail "$audit_dir/restore.sqlite" "PRAGMA integrity_check; PRAGMA foreign_key_check;"
sqlite3 -bail "$audit_dir/restore.sqlite" "SELECT (SELECT count(*) FROM portal_access WHERE active=0), (SELECT count(*) FROM portal_records WHERE kind='worship'), (SELECT count(*) FROM portal_photos);"
shasum -a 256 "$audit_dir/backup.sql"
```

預期第一條檢查印出 `ok`，外鍵無錯誤。第二條檢查在這份測試資料為 `1|1|0`。備份本身不進 Git 或 Obsidian。

## 費用與下一個門檻

- [Workers Free](https://developers.cloudflare.com/workers/platform/limits/) 和 [D1 Free](https://developers.cloudflare.com/d1/platform/pricing/) 有用量上限；保留 Free，避免超量時切換付費方案。不要為試站購買網域。
- [R2 Standard](https://developers.cloudflare.com/r2/pricing/#free-tier) 有免費用量，但帳戶總用量超額會計費。[預算警報](https://developers.cloudflare.com/billing/manage/spend-management/) 是寄通知，不是阻擋 R2 扣款。要求零額外扣款時，就不接上有超額計費可能的 R2；如果帳戶只為此專案開通 R2、又不打算用，可由帳戶持有人在帳單頁自行檢查及取消訂閱，切勿刪除其他用途的資源。
- 還沒有正式或測試網址給家人使用。下一階段先在 Zero Trust **Free** 用自己的測試信箱驗證 Access、來源與唯讀角色；後台要求升級、付費或不清楚條款就停。照片這階段只做本機驗收。沒有完成雲端登入、角色與照片儲存及還原驗收，不切換正式站，也不搬用原資料。
