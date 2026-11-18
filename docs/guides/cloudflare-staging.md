# 許氏入口網 Cloudflare 測試站：操作紀錄與零扣款檢查

2026-11-16 起（台北時間） · 測試站初版 `a2c2761` · [開發者手冊](developer.md) · [家人手冊](user.md) · [CI/CD 照做手冊](ci-cd.md)

## 結論先看

[開啟 Cloudflare 私人測試站](https://hsu-family-staging.helloapple080.workers.dev/) → 按 **Cloudflare** → 使用原本的 Cloudflare 帳號。沒有另設網站密碼，不寄一次性登入碼，不開放註冊；目前只有持有人能登入。測試站連的是雲端 D1，未搬入正式資料，原先的演練紀錄已不在；不需要重新灌假資料才能查看介面。請勿輸入正式資料或照片；正式站、MongoDB、既有照片未動。

## 實際做了什麼

1. **準備程式。** 沿用 React 介面，改由 Vite 建置；Worker 分開管登入、權限與資料。保留 Next.js／Supabase 回退和 GitHub Pages 假資料展示。程式在 `codex/cloudflare-react`；[GitHub CI](https://github.com/helloapple080/HsuFamily/actions/runs/43775310531) 已通過。
2. **授權 CLI。** 我啟動 Wrangler，你在 Cloudflare 瀏覽器按允許。第一次交換憑證逾時；查到 IPv6 不通，IPv4 能連線，改用 IPv4 優先後重新授權成功。`wrangler whoami` 已確認登入；文件不公開信箱、權杖、帳戶編號或資料庫 UUID。
3. **檢查費用。** 後台 Workers plans 顯示 Free 是目前方案；R2 Overview 顯示已開通，但沒有桶，使用量為零。這次沒有升級、購買網域、建 R2 桶或開 Images；部署後再查看 R2，當期應計費金額仍顯示 US$0.00。
4. **驗證本機建置。** 用 `pnpm check:cloudflare` 做 Vite 建置和部署乾跑，再執行 `pnpm test`。148 項通過，含權限、農曆、照片驗證和競爭寫入。本機照片測試不等於雲端照片驗收。
5. **建立隔離資料庫。** 確認雲端沒有同名 D1 後建立 `hsu-family-staging`（亞太區）；兩個遷移 `0001_portal.sql`、`0002_photos.sql` 均成功。雖已有照片資料表，但沒有 R2 綁定或照片。
6. **上傳 Worker。** 用 Git 忽略的 `wrangler.staging.local.jsonc` 乾跑，確認只連測試 D1，再用相同設定部署。不開 `workers.dev`、預覽網址、網域或排程。Cloudflare 回報 `No targets deployed`：程式已上傳，沒有對外網址。
7. **假資料與還原。** 匯入假規則和停用帳號；補齊樣本標題、閏月規則和備註後重跑同一筆。匯出三張應用表，在空白的本機 SQLite 還原。完整性檢查為 `ok`、無外鍵錯誤，帳號／規則／照片筆數為 `1 / 1 / 0`，日期為農曆八月十五。這次沒有雲端照片可用來驗收圖片備份。
8. **查下一步、不提交結帳。** Zero Trust 後台仍顯示「Welcome」和「Get started」，尚未建立組織。本次只查看，沒有開通方案或放行網址。
9. **查 US$0 是否能止扣。** Cloudflare 的「Spending limit」可停止 Workers 或 Durable Objects 的部分作業，必須填大於 US$0，且不涵蓋 R2；「Budget alert」只寄通知，不停止用量。不設定無法達成目的的警報，也不升級方案。重新乾跑測試站，確認只有測試 D1 綁定，沒有 R2、Images、公開路由或排程。
10. **準備免費登入、停在最後一頁。** 在 Zero Trust 按「Get started」，選擇 Free，進到「Activate Zero Trust Free」。畫面顯示應付 US$0／月，但另列有超出免費額度可向卡片扣款的授權。我沒有勾選同意，也沒有按啟用。測試站仍未開放網址。
11. **核對帳戶實際設定。** 到 Billing → Spend limits，頁面只列 Workers 與 Durable Objects，兩者目前都是 Free tier；沒有 R2、Zero Trust 或帳戶總額的止扣設定。Billable usage 當期顯示 US$0.00，已有一個 US$10 的 Budget alert。這是既有通知門檻，不是扣款上限；未更動警報、付款授權或方案。
12. **確認帳戶持有人啟用免費登入服務。** Zero Trust 後台顯示 `Zero Trust Free`。持有人指定先只用自己的 Cloudflare 登入信箱。用 `Allow → Include → Emails` 建立 `hsu-family-staging-owner` 政策，只放一個精確比對的信箱，不使用網域、`Everyone` 或 `Bypass`。
13. **先加鎖，再開測試網址。** 在測試 Worker 的 Access 頁把上述政策套用到 `All traffic`，再啟用其固定 `workers.dev` 網址，預覽網址仍關閉。未登入的首頁與 API 都回 `302` 並導向 Access。確認保護生效後，才在測試 D1 新增一位管理員，填入測試專屬的簽發者、應用 AUD 及來源網址，再部署一次。部署清單只有 D1 和三個驗證設定，沒有 R2 或 Images 綁定。
14. **在雲端實測兩條登入路徑。** 無登入首頁、身分 API、拜拜 API、偽造 Access 標頭與未登入的跨站 POST 都被 Access 擋下，回 `302`，沒有傳回資料。原本已登入的 Cloudflare 帳號點「Cloudflare」後可進站；畫面顯示「管理員」，D1 確認只有一位啟用且已綁定身分的管理員，另一個示例帳號仍停用。相同假規則「農曆八月十五」在 2026 顯示 `2026-09-25`，2036 顯示 `2036-10-04`；資料庫仍有一筆假規則、零張照片。
15. **模擬手機驗收並修正矮螢幕。** 用 Chrome 的 390×844、320×568 直式視窗檢查選單、拜拜日期、活動月曆換月、成員及行程。月曆只有格子內左右捲；整頁不會橫向捲。568×320 橫式發現新增行程表單超出畫面，修正為表單內可上下捲。Lint、型別、148 項測試和打包通過；只部署到隔離 Worker，重跑原尺寸並確認空白儲存會提示必填、取消可按，沒有新增資料。這是瀏覽器尺寸模擬，不等於 iPhone 或 Android 實機驗收。
16. **分開記錄檢查、發布與回復。** `516fb08` 的工作分支 CI 通過；公開展示和 CodeQL 是 `main` 的另一版本。重查匿名首頁及 API 都導向 Access，公開展示首頁可用且沒有私人 API，測試 D1 沒有待套遷移。整理成 [CI/CD 照做手冊](ci-cd.md)，本次只更新文件，不部署 Worker、不合併 `main`。
17. **實測兩頁同時編輯會不會互蓋（2026-11-17）。** 同一管理員在兩個頁籤編輯「假資料：還原演練」。A 先儲存，B 用舊版本送出時看到衝突提示且保留草稿；重新整理只顯示 A。D1 查核版本由 `1` 增至 `2`，B 沒有覆寫。用表單還原空白備註後，版本正常增至 `3`；原本的農曆八月十五、一般月份規則仍在，依然只有一筆假規則、零張照片、一位啟用管理員，示例家人仍停用。沒有新增白名單、調整費用設定或變更正式站。重做步驟見 [CI/CD 照做手冊](ci-cd.md)第 4 節。
18. **把未完成的權限情境先做成本機自動驗收。** 用合成權杖和不留存資料的本機 D1，確認唯讀家人可讀不能新增、修改、刪除，未核准帳號不可讀，停用後相同的未過期權杖不可讀，以及管理員跨站寫入被拒。這些測試自動納入 CI，重做步驟見 [CI/CD 照做手冊](ci-cd.md)第 2 節；沒有新增 Access 白名單或寫入雲端資料。真人帳號與手機仍是另外的驗收關卡。
19. **修正行事曆版面與深淺色（2026-11-18）。** 補回按鈕、邊框與焦點的主題色，讓頁籤清楚標出目前檢視；拜拜日期區有閱讀寬度上限，側欄的三個主題選項等寬。用假資料在 Chrome 檢查桌面 1226×698、直式 390×844／320×568 和橫式 568×320；可切換主題、開關表單，窄螢幕只讓活動月曆格子左右捲。CI 加入編譯後的 CSS 檢查，部署按 [CI/CD 照做手冊](ci-cd.md)第 4 節，僅限隔離站。
20. **發布並重驗隔離站（2026-11-18）。** `a8c6fe5` 的 [CI](https://github.com/helloapple080/HsuFamily/actions/runs/44338929496) 通過後，確認沒有待套遷移，只發布隔離 Worker。舊版 `e862d426…` → 新版 `fb8f7f0f…`，新版承接 100% 流量。部署後匿名首頁、身分與拜拜 API 仍回 `302`；管理員重開後可看到假拜拜日期、桌面頁籤底色與手機選單。沒變更 D1、Access 白名單、正式站或費用方案；這仍是瀏覽器尺寸驗收。
21. **部署家用備忘（2026-11-19）。** `100bf80` 的 [CI](https://github.com/helloapple080/HsuFamily/actions/runs/44343814555) 通過；先匯出隔離 D1 並在新 SQLite 成功還原、核對完整性和外鍵，再只套用 `0003_household_notes.sql`。遷移後再次匯出並還原；停用帳號／拜拜規則／照片／備忘筆數為 `1 / 1 / 0 / 0`。隔離 Worker `fb8f7f0f…` → `db82dcf4…`，新版承接 100% 流量。匿名首頁、備忘頁與 API 各回 `302`；核准管理員能讀既有拜拜規則，並可開啟、取消手機尺寸下的備忘表單。備忘沒有寫入範例；正式資料、Access 白名單與費用方案不變。唯讀家人和寫入衝突已在本機跑過，尚未用另一個真實帳號驗收。
22. **部署手機導覽、下一次拜拜與備忘日期（2026-11-19）。** `e09ccab` 的 [CI](https://github.com/helloapple080/HsuFamily/actions/runs/44351620775) 通過，本機 163 項測試。隔離 D1 匯出後在本機還原成功；在還原副本加入一筆版本 4 的測試備忘後試套 `0004`，原文字和版本不變，兩個日期預設為空。遠端只套 `0004_household_dates.sql`，套用前後帳號／紀錄／照片／備忘總筆數同為 `2 / 0 / 0 / 0`，只有原先一位啟用管理員。隔離 Worker `db82dcf4…` → `c156f8c6…`，承接 100% 流量。匿名首頁、備忘頁和備忘 API 都回 `302`；管理員以 390×844 開啟備忘、確認兩個日期欄並取消，沒有寫入。沒有改 Access、R2、Images、費用方案、`main` 或正式站。iPhone、Android 實機及第二位核准家人尚未驗收。

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

隔離站的測試備份不得含正式資料。匯出時 Wrangler 可能顯示短期有效下載連結；不要貼到聊天、Git 或共用文件。若正式資料要備份，應使用加密儲存，並另行備份圖片本體；D1 的 SQL 不含圖片。以下例子適用已經套用 `0003_household_notes.sql` 的隔離站；要做 0003 的**遷移前**備份，先從匯出和核對指令移除尚未建立的 `portal_household_notes`。

```sh
umask 077
audit_dir=$(mktemp -d)
pnpm exec wrangler d1 export DB --remote --config wrangler.staging.local.jsonc \
  --output "$audit_dir/backup.sql" --table portal_access portal_records portal_photos portal_household_notes
sqlite3 -bail "$audit_dir/restore.sqlite" < "$audit_dir/backup.sql"
sqlite3 -bail "$audit_dir/restore.sqlite" "PRAGMA integrity_check; PRAGMA foreign_key_check;"
sqlite3 -bail "$audit_dir/restore.sqlite" "SELECT (SELECT count(*) FROM portal_access WHERE active=0), (SELECT count(*) FROM portal_records WHERE kind='worship'), (SELECT count(*) FROM portal_photos), (SELECT count(*) FROM portal_household_notes);"
shasum -a 256 "$audit_dir/backup.sql"
```

預期第一條檢查印出 `ok`，外鍵無錯誤。第二條記下實際筆數，和遷移後比較；不是固定的樣本數。備份本身不進 Git 或 Obsidian。只套 `0003` 時，新備忘表為 0 筆；只套 `0004_household_dates.sql` 時，先在本機還原副本執行該 SQL，確認原紀錄和版本不變，兩個新欄位預設為空。確認遠端待套檔名正確後再執行 `migrations apply`，核對沒有待套遷移、既有表筆數不變，再部署 Worker。不必重跑舊的拜拜資料匯入。參考 [Cloudflare D1 匯入與匯出說明](https://developers.cloudflare.com/d1/best-practices/import-export-data/)。

### 重做登入與固定網址

1. 在 Zero Trust → Access controls → Policies 新增可重用政策。動作 `Allow`，條件 `Include → Emails`，只加入核准的測試信箱；在測試 Worker → Access 選 `All traffic`，套用該政策。
2. 在測試設定啟用 `workers_dev: true`，保留 `preview_urls: false`，先不放 `vars`。部署後，用無登入瀏覽器或 `curl -I` 確認首頁與 `/api/hosted/auth/me` 導向正確的 Access 登入頁。如果沒有被擋，立刻關回 `workers_dev`，修好再繼續。
3. 維運者只在測試 D1 新增核准信箱的 `Admin`，不用猜測或預填 Access subject。從測試 Worker → Access 取得 AUD 與 JWKS；JWKS URL 的來源即簽發者。依[開發者手冊](developer.md)設定 `ACCESS_AUDIENCE`、`ACCESS_ISSUER`、`PORTAL_ORIGIN`，重新部署並重做匿名檢查。第一次成功登入後核對 `portal_access` 角色、啟用、身分綁定狀態；不要把信箱或設定值放進文件和 Git。
4. **要關閉測試站：** 先把 `workers_dev` 關回 `false`，維持 `preview_urls: false`，重新部署，再確認固定網址不可用。若只撤銷某個帳號，先把測試 D1 的該帳號 `active` 設為 `0`，再從 Access 政策移除其信箱；還有有效的 Access 工作階段時，D1 的停用仍會拒絕 API。不要刪除正式服務或原始資料。

## 費用與下一個門檻

- [Workers Free](https://developers.cloudflare.com/workers/platform/limits/) 和 [D1 Free](https://developers.cloudflare.com/d1/platform/pricing/) 有用量上限；保留 Free，避免超量時切換付費方案。不要為試站購買網域。
- [R2 Standard](https://developers.cloudflare.com/r2/pricing/#free-tier) 有免費用量，帳戶總用量超額會計費。[Cloudflare 支出控制說明](https://developers.cloudflare.com/billing/manage/spend-management/) 指出真正的止扣上限不涵蓋 R2，也不保證帳單上限；[預算警報](https://developers.cloudflare.com/billing/manage/budget-alerts/) 只寄通知。要求零額外扣款時不連接 R2；若帳戶只為此專案開通 R2、又不打算用，可由帳戶持有人在帳單頁檢查及取消訂閱，切勿刪除其他用途的資源。
- Zero Trust Free 的「今天 US$0」是方案價格，不是帳戶零元止扣設定。[官方開通說明](https://developers.cloudflare.com/cloudflare-one/setup/) 與[官方席次說明](https://developers.cloudflare.com/cloudflare-one/team-and-resources/users/seat-management/)可核對方案與登入上限。本次僅用一席測試，沒有升級或加購；不接受額外計費服務時，維持 R2 和 Images 未連接。
- **還不能切正式站。** 未使用第二個真實信箱驗證未白名單與唯讀家人，雲端尚未實測撤權和已登入的跨站寫入，也沒有雲端照片上傳／備份與正式資料搬移。這些不能用本機測試或已通過的匿名檢查代替。需要操作公開版時，仍可用[唯讀假資料展示](https://helloapple080.github.io/HsuFamily/)。
- **下一步：** 用真手機登入隔離站，確認底部導覽、月曆左右滑、備忘表單鍵盤與日期選擇器；不需寫入正式資料。第二位測試者的信箱尚未核准，白名單維持單人；之後有核准帳號再驗證唯讀與撤權。費用限制維持不接 R2／Images、不搬正式資料、不切換正式站。
