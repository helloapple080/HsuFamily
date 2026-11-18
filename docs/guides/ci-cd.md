# 許氏入口網 CI/CD：從改程式、檢查到部署與回復

[開發者手冊](developer.md) · [家人使用手冊](user.md) · [Cloudflare 實作紀錄](cloudflare-staging.md) · [瀏覽器版](ci-cd.html)

CI 是「送出程式後自動檢查」；CD 是「把通過檢查的版本交付到網站」。**在這個專案，CI 綠燈不等於私人站已更新。** 目前保留 Cloudflare Workers Free＋Access＋D1；不新增部署權杖、不接 R2／Images、不切換正式站。Vercel／Supabase 只保留作回退評估，不是這條發布流程；Resend 和自訂網域目前不用。

## 1. 先認清三個目的地

1. **工作分支 `codex/cloudflare-react`：** 推送會跑程式檢查，不會發布公開展示或私人站。程式和本文以這個分支為準，`main` 不是同一個版本。
2. **公開 [GitHub Pages 展示站](https://helloapple080.github.io/HsuFamily/)：** 對 `main` 送出 PR 會組建及檢查展示；只有程式推到 `main` 且組建成功，才自動部署。永遠只放虛構資料、唯讀、不含登入或私人 API。不要為了更新私人測試站推送 `main`。
3. **私人 [Cloudflare 測試站](https://hsu-family-staging.helloapple080.workers.dev/)：** 維運者確認 CI 綠燈後，用本機 Wrangler 手動發布；Access 白名單、D1、Worker 都是測試專用。沒有 GitHub → Cloudflare 自動部署，也沒有正式切換。

## 2. 第一次準備與日常開發

準備 Git、Node.js 22、pnpm 10.33.0；GitHub CLI 是選用工具，不裝也能從 GitHub 的 Actions 頁操作。已在專案資料夾就跳過 `git clone`。切分支前先看 `git status`，有自己的未提交變更先保留；不要用強制覆寫。

```sh
git clone https://github.com/helloapple080/HsuFamily.git
cd HsuFamily
git switch codex/cloudflare-react
git pull --ff-only
node --version
pnpm --version
```

若 `pnpm --version` 不是 `10.33.0`，請先用已安裝的 Corepack 啟用對應版本，或從 `frontend` 開一個臨時 shell，把固定版本放在 PATH 前面：`npx --yes --package=pnpm@10.33.0 -c 'zsh'`（沒有 zsh 可改 `sh`）；在該 shell 重查版本後再繼續。不要直接允許其他 pnpm 版本清掉現有 `node_modules`，也不要為了解決版本錯誤而改鎖檔。

在 `frontend` 執行：

```sh
pnpm install --frozen-lockfile --ignore-scripts
pnpm audit:prod
pnpm lint
pnpm test
pnpm check:cloudflare
pnpm build
pnpm test:csp
pnpm build:pages && pnpm test:pages
```

每行都應成功才繼續。`check:cloudflare` 在本機產生 Vite 靜態檔、檢查按鈕／邊框等主題色和深色變體確實出現在 CSS，再做 Worker 打包乾跑，不會部署。改介面時另用瀏覽器檢查深淺色和桌面／手機尺寸；若主題樣式檢查失敗，先確認 `globals.css` 將現有色票連到 Tailwind 的語義類別。 `build` 是檢查 Next.js 回退版，`build:pages` 則產生唯讀展示。若只改文件，專案根目錄另跑 `ruby docs/guides/render.rb`、`ruby docs/guides/render.rb --check`、`git diff --check`，Markdown 與對應 HTML 要一起提交。

要單獨重跑 Cloudflare 私人 API 的權限演練，在 `frontend` 執行：

```sh
pnpm test edge/tests/access-lifecycle.test.ts
```

預期 4 項通過。這個測試用合成簽章權杖、正式 Worker 的 API 程式和 [Wrangler 本機 D1](https://developers.cloudflare.com/workers/wrangler/api/#getplatformproxy)，驗證唯讀家人拒寫、未核准拒讀、停權後舊權杖拒讀、以及管理員跨站寫入遭拒；同時確認原資料沒被改。完整 `pnpm test` 和 CI 都會執行它。無需 Cloudflare 登入、真實信箱或雲端 D1，更不是 Access 登入頁或實際手機驗收。

改完用 `git status`、`git diff` 確認只有這次的檔案，再用 `git add <這次修改的實際檔案>`、英文 Conventional Commit 訊息提交，最後執行 `git push origin codex/cloudflare-react`。不要加入 `.env`、`wrangler.staging.local.jsonc`、權杖、D1 匯出、驗證碼、信箱或原始照片。新開的一般工作分支不在 CI 的推送名單裡；若要走 PR 檢查，先核對目標為 `main`，但不要未經驗收就合併。

## 3. 在 GitHub 看結果與排除失敗

打開 [Actions → CI](https://github.com/helloapple080/HsuFamily/actions/workflows/ci.yml)，用分支選 `codex/cloudflare-react`，點最新一筆，對照頁面上的提交 SHA 是否等於 `git rev-parse HEAD`。應看到 `Frontend`、`Supply Chain` 都綠燈。也可在已登入 `gh` 的專案根目錄執行：

```sh
gh run list --workflow ci.yml --branch codex/cloudflare-react --commit "$(git rev-parse HEAD)" --limit 5
# 把下方 123456789 換成上一行對應的執行編號；不是提交 SHA。
gh run watch 123456789 --compact --exit-status
```

**目前工作分支內有哪些自動化：**

- `.github/workflows/ci.yml`：推送 `main`、`codex/supabase-portal`、`codex/cloudflare-react`，或 PR 目標為 `main` 時執行。`Frontend` 固定 Node 22、依鎖檔安裝，稽核正式依賴、Lint、測試、Cloudflare 乾跑，然後建置 Next.js、核對私人 API 路由與 CSP，最後驗證 Pages 靜態產物。`Supply Chain` 用 Trivy 阻擋 HIGH／CRITICAL，並輸出 CycloneDX SBOM；可在該次執行頁下方 Artifacts 下載 `sbom-cyclonedx`，保留 30 天。SBOM 是元件清單，不是安全通過證明。
- `.github/workflows/codeql.yml`：`main` 的推送、以 `main` 為目標的 PR、以及預設分支每週排程會掃描 JavaScript／TypeScript；一般工作分支的 push 不會執行。從 [CodeQL 執行紀錄](https://github.com/helloapple080/HsuFamily/actions/workflows/codeql.yml)和 Security → Code scanning 查看；掃描執行成功不表示沒有告警。
- `.github/workflows/pages.yml`：在 `main` 的 push／PR 先測試、Lint、組建並驗證 `frontend/out`；PR 不上傳、不發布。`main` 的 push 才上傳 Pages artifact，並交由 `github-pages` 環境部署。GitHub 目前 Settings → Pages → Source 為 GitHub Actions，部署權限只在 Pages 部署工作使用。
- `.github/dependabot.yml`：每週檢查 `frontend` 套件及 GitHub Actions 版本，開更新 PR；不會自行合併或發布。應檢查更新內容、鎖檔、CI 與安全結果再決定是否接受。

**紅燈怎麼辦：** 打開失敗的工作，先讀第一個失敗步驟。Lint／Test 紅燈，在本機跑同名指令重現；依賴稽核／Trivy 紅燈先確認套件或檔案，不要降低嚴重度門檻；Pages 紅燈先重跑 `pnpm build:pages && pnpm test:pages`，不要略過靜態檔驗證。如果只是網路或 runner 暫時錯誤，在同一執行頁選 **Re-run failed jobs**，或執行 `gh run rerun 123456789 --failed`。程式有問題則修好、提交新 SHA 再推，不要部署失敗或取消的版本。

**CI 版本維護（2026-11-17）：** 兩個 CI 工作都固定使用 `ubuntu-26.04`，避免隨 [GitHub 的 `ubuntu-latest` 別名](https://github.com/actions/runner-images/issues/14748) 切換作業系統版本。`Upload SBOM` 更新到 [GitHub 官方 `upload-artifact` v7.0.2](https://github.com/actions/upload-artifact/releases/tag/v7.0.2)，以完整提交 SHA 固定；這個動作使用 Node.js 24，專案的建置仍用 Node.js 22。後續更新時，在工作分支推送，確認兩個 CI 工作通過，並下載該次的 `sbom-cyclonedx`，核對壓縮檔內有 `sbom.cdx.json`。不需要部署 Worker，也不影響 Pages 或 CodeQL 的設定。

目前 `main` 未啟用強制通過檢查的分支規則；上面的綠燈是維運者必須手動把關的條件，GitHub 不會替你阻擋所有不合規合併。要變更合併規則時先核對實際回報的檢查名稱，並另外驗證 Pages 發布。

這是公開儲存庫，現有工作使用 GitHub 標準代管 runner，依 [GitHub Actions 計費說明](https://docs.github.com/en/billing/concepts/product-billing/github-actions)不收這類運算分鐘費。不要改用較大型付費 runner，也不要把資料或憑證放到公開日誌、產物或分支。

## 4. 私人測試站更新：只部署 CI 已通過的同一個 SHA

以下只適用於**已建立及保護的** `hsu-family-staging`。首次建立、設定 Access、確認 Free 方案，請照 [Cloudflare 實作紀錄](cloudflare-staging.md)；如果沒有 `wrangler.staging.local.jsonc` 或不是該帳戶，先停，不可改用共用 `wrangler.jsonc` 執行遠端部署。本節只有第 3 步會發布新程式；發布命令不會變更 D1 資料，只有下方的編輯演練會暫時修改一筆假資料。

1. 從乾淨工作樹確認 `git rev-parse HEAD` 是 Actions 綠燈的同一個 SHA。在 `frontend` 執行前置檢查；`whoami` 若未登入，才執行 `pnpm exec wrangler login`，由帳戶持有人在瀏覽器授權，再重跑。

```sh
git status --short
git rev-parse HEAD
git check-ignore wrangler.staging.local.jsonc
pnpm exec wrangler whoami
pnpm build:cloudflare
pnpm exec wrangler deploy --dry-run --config wrangler.staging.local.jsonc
pnpm exec wrangler deployments status --config wrangler.staging.local.jsonc
pnpm exec wrangler d1 migrations list DB --remote --config wrangler.staging.local.jsonc
```

2. 核對乾跑輸出：Worker 與 D1 名稱是 `hsu-family-staging`，資料庫綁定只有 `DB`，其他只有 Access／來源的三個變數；`preview_urls` 為 `false`，無 R2、Images、排程或自訂網域。抄下目前 Worker version ID 到私密維運筆記。若發現待套 D1 遷移，不要接著執行下方部署：先做測試備份和還原演練，確認新舊版本與資料結構相容，才依隔離手冊另行套用及驗證。純前端、文件更新不要重跑資料遷移。
3. 確認沒有要回復或升級的提示後，僅在真的有私站程式變更時執行發布。文件更新不必部署。

```sh
pnpm exec wrangler deploy --config wrangler.staging.local.jsonc
```

4. 用**無痕瀏覽器**開首頁，應先出現 Cloudflare 登入頁；再檢查下列無登入請求，預期兩行都是 `302`。不用 `-L`，也不要貼含暫時登入參數的完整回應標頭。

```sh
stage_url='https://hsu-family-staging.helloapple080.workers.dev'
curl -sS --max-time 15 -o /dev/null -w '%{http_code}\n' "$stage_url/"
curl -sS --max-time 15 -o /dev/null -w '%{http_code}\n' "$stage_url/api/hosted/auth/me"
```

5. 白名單管理員登入，確認身分正確、原有行事曆資料可讀；依改動測行事曆、行程或手機表單。若是家用備忘版本，另以無登入狀態驗證 `/household`、`/api/hosted/household` 都回 `302`；登入後從選單打開，初次部署應看到空白狀態，無須把假資料寫入這一區。寫入與唯讀角色先用本機隔離測試；真實家人帳號須另驗。記下「日期／Git SHA／CI 連結／前後 Worker 版本／測試結果／仍未驗收項目」。不要把雲端登入、角色或照片驗收用 CI 綠燈帶過。

6. 備忘的 `0003_household_notes.sql` 僅新增資料表，舊版 Worker 仍可用。遇到前端錯誤時，可回退 Worker 至遷移前相容版本，備忘會暫時不可見但仍保留在 D1；不要刪表或還原覆蓋現有 D1。依 [隔離站手冊](cloudflare-staging.md)先備份含 `portal_household_notes` 的新結構。

### 有改儲存或版本控制時：驗證舊編輯不會蓋掉新資料

1. 用同一位白名單管理員，在兩個頁籤開啟隔離站。先記下「假資料：還原演練」的原始備註，兩頁都按這筆的「編輯」。若找不到這筆假規則，停止演練，不要改用真實資料。
2. 兩頁各填不同備註（例如「驗收 A」、「驗收 B」）。先在 A 頁籤按「儲存規則」，確認清單顯示 A。接著在**未重新整理**的 B 頁籤儲存。
3. B 應看到「其他人已更新這筆資料」的衝突提示（HTTP `409`），表單保留 B 的草稿。重新整理 B 頁面後，清單應只看到 A，沒有 B。若要保留 B 草稿，請先複製再重新整理。
4. 在已重新整理的頁籤，用表單把備註改回原值，再重新開啟編輯核對。這是一次正常儲存，版本會再增加；不用手動下調版本。若看到不是這次演練寫入的內容，停止，不要覆蓋。這只驗證同筆資料的寫入衝突，不代替兩個帳號的權限驗收。

每次發布可直接複製這份紀錄格式（不放信箱、權杖或資料庫內容）：

```text
日期／環境：
Git SHA／CI 執行網址：
Worker 前一版本 → 新版本：
匿名與管理員驗證：
資料庫遷移／回復方式：
尚未驗收項目：
```

## 5. 公開展示發布與兩種回復

- **公開展示**：有意更新展示才走 PR 審查與 `main` 合併，先確認 PR 的 CI、Pages 建置與 CodeQL 檢查。合併後到 [Pages 工作流](https://github.com/helloapple080/HsuFamily/actions/workflows/pages.yml) 看 `Build Pages Demo`、`Deploy Pages Demo` 兩個工作，以首頁 `200`、`/HsuFamily/api/hosted/auth/me` `404` 再驗證。發布錯版時，用新的 PR 撤銷那次程式變更，再走同一路徑重新發布；不要強制推送或把私人出口改指到 Pages。
- **私人測試 Worker**：若只是程式錯版且 D1 相容，Cloudflare → Workers & Pages → `hsu-family-staging` → Deployments 選上次已驗證的版本，使用 Rollback；CLI 也可用下方命令。回復後重做匿名及管理員驗證。它回復的是程式、資產及版本設定，**不會把 D1 資料或 Access 政策倒回去**。[Cloudflare Worker 回復說明](https://developers.cloudflare.com/workers/versions-and-deployments/rollbacks/)

```sh
pnpm exec wrangler deployments list --config wrangler.staging.local.jsonc
# 把 PREVIOUS_VERSION_ID 換成事前記錄、相容且已驗證的 Worker UUID，再確認提示。
pnpm exec wrangler rollback PREVIOUS_VERSION_ID --config wrangler.staging.local.jsonc
```

如果未登入居然看得到私人內容、目標接錯，或 D1 遷移無法回復，先停寫並關閉測試站的 `workers.dev` 路由。Access 登入保護要保留，預覽網址要保持關閉，不要為了讓畫面恢復而停用登入保護。先保全資料與紀錄，依 [Cloudflare 實作紀錄](cloudflare-staging.md)處理備份及隔離還原，不要自動覆蓋資料庫。

## 6. 本次核對與下一個驗收點

2026-11-17（台北時間）：工作分支 `516fb08` 的 [CI](https://github.com/helloapple080/HsuFamily/actions/runs/43899310996) 已通過；公開展示的 [`main` 發布](https://github.com/helloapple080/HsuFamily/actions/runs/30146143974)及 [CodeQL 排程](https://github.com/helloapple080/HsuFamily/actions/runs/42736931107) 取自另一個提交 `bbe6168`，不算工作分支的 CodeQL 結果。重新驗證公開首頁 `200`／私人路徑 `404`、Cloudflare 匿名首頁／身分／拜拜 API `302`；測試 Worker 單一版本承接 100% 流量，D1 沒有待套遷移。本次文件更新不需要重新發布 Worker。

2026-11-19（台北時間）：家用備忘 `100bf80` 的 [CI](https://github.com/helloapple080/HsuFamily/actions/runs/44343814555) 通過，本機 157 項測試通過。隔離站已按第 4 節發布：先備份還原再套用 `0003_household_notes.sql`，重新核對備忘 0 筆及既有資料筆數；Worker `fb8f7f0f…` → `db82dcf4…`，匿名與管理員空白頁驗收通過。沒有合併 `main`、部署公開展示或切換正式站。仍待真機和另一核准家人帳號驗收；詳細操作見[隔離站紀錄](cloudflare-staging.md)。

同日隔離站實測：A 儲存後 D1 版本 `1 → 2`；B 被拒、草稿留在表單，D1 仍是版本 `2` 且保留 A。重新整理可見 A，還原空白備註後 D1 版本為 `3`。仍是一筆假規則、零張照片，白名單未變。

下一步仍是實機手機與第二個經本人核准的帳號，驗證唯讀、拒寫與撤權；白名單未擴大前，不會把這些標成通過。照片另作決策：硬性避免超額扣款時，不接 R2／Images；若照片優先且能用 Supabase Free 的 1 GB，再獨立驗證既有回退版，不直接搬正式資料。
