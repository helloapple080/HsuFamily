# Verification, Documentation, and Release Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 以真實瀏覽器、遠端 CI 與可追溯文件證明修復結果，更新長官可讀的 Open Design 簡報與 Obsidian，最後安全推送 GitHub。

**Architecture:** Playwright 以 mock API 驗證 deterministic UI／a11y，再以 GitHub Actions 執行完整 backend、frontend、CodeQL 與 supply-chain gates。Markdown 是稽核 source of truth，簡報只摘要證據；Obsidian 是核准後的同步副本。

**Tech Stack:** Playwright Chromium、`@axe-core/playwright`、GitHub Actions、GitHub CLI、Markdown、PowerPoint／HTML slide tooling、Obsidian vault。

## Global Constraints

- 不以「應該通過」代替實際命令、瀏覽器截圖或 GitHub run 證據。
- 不連線修改 MongoDB；E2E 使用 mock API，正式資料驗收另依部署手冊執行唯讀檢查。
- 不將 secret、cookie token、正式資料或個資寫入 screenshot、log、文件或 artifact。
- 文件區分 repository 已完成控制與外部環境待完成控制。
- 簡報文字使用繁體中文，技術細節以風險、影響、成果、殘餘責任呈現。
- Git commit 使用 Conventional Commits；只有驗證全綠的批次才推送。

---

## File Map

- `frontend/playwright.config.ts`：瀏覽器專案與 web server。
- `frontend/e2e/accessibility.spec.ts`：login／portal axe 與鍵盤驗收。
- `frontend/e2e/responsive.spec.ts`：375／768／1024／1440 viewport、44px targets。
- `frontend/e2e/authorization.spec.ts`：normal／admin CRUD visibility。
- `frontend/e2e/helpers/mockApi.ts`：固定 mock response，無真實個資。
- `.github/workflows/ci.yml`：Playwright job 與 artifacts。
- `docs/03-安全風險與修復台帳.md`：finding → fix → evidence → residual risk。
- `docs/04-部署與維運手冊.md`：TLS、secret、backup/restore、rollback、monitoring。
- `docs/05-企業級成熟度與後續路線圖.md`：修復後成熟度判定。
- `docs/07-Skills全面自檢報告.md`：所有 High／Medium／Low 狀態。
- `docs/08-全面修復驗收報告.md`：本次驗證命令、commit 與 CI run。
- `docs/presentation/OPEN-DESIGN.md`：簡報可編輯 source。
- `docs/presentation/HsuFamily-Enterprise-Readiness.pptx`：長官簡報。
- `/Volumes/USB/阿豬程式資料/HsuFamily-Obsidian/許氏入口網/`：核准的文件副本。

### Task 1: Add Deterministic Playwright and axe Tests

**Files:**
- Modify: `frontend/package.json`
- Modify: `frontend/pnpm-lock.yaml`
- Create: `frontend/playwright.config.ts`
- Create: `frontend/e2e/helpers/mockApi.ts`
- Create: `frontend/e2e/accessibility.spec.ts`
- Create: `frontend/e2e/responsive.spec.ts`
- Create: `frontend/e2e/authorization.spec.ts`

**Interfaces:**
- Produces: `pnpm test:e2e`; mock API helper accepts role `User | Admin` and serves auth, events, members and itineraries.

- [ ] **Step 1: Add the initially failing E2E script**

```json
{
  "test:e2e": "playwright test",
  "test:e2e:report": "playwright show-report"
}
```

Run `pnpm test:e2e`; expected: command fails because Playwright config and package are absent.

- [ ] **Step 2: Install browser test dependencies**

```bash
cd frontend
pnpm add -D @playwright/test @axe-core/playwright
pnpm exec playwright install chromium
```

Create config:

```ts
import { defineConfig, devices } from '@playwright/test'

export default defineConfig({
  testDir: './e2e',
  fullyParallel: true,
  retries: process.env.CI ? 2 : 0,
  reporter: [['html', { open: 'never' }], ['list']],
  use: {
    baseURL: 'http://127.0.0.1:3100',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }],
  webServer: {
    command: 'pnpm dev --hostname 127.0.0.1 --port 3100',
    url: 'http://127.0.0.1:3100/login',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
```

- [ ] **Step 3: Implement mock API helper**

Expose:

```ts
export async function mockPortalApi(page: Page, role: 'User' | 'Admin'): Promise<void>
```

Route `**/api/auth/me`, `**/api/events**`, `**/api/members**`, `**/api/itineraries**` with `ApiResponse` fixtures using names `測試成員`／`家庭聚會`, never real family data. Route SignalR negotiation to a deterministic failed connection so the disconnected status can be asserted without a backend.

- [ ] **Step 4: Add axe and keyboard checks**

```ts
test('login and calendar have no serious accessibility violations', async ({ page }) => {
  await page.goto('/login')
  let results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter(v => ['critical', 'serious'].includes(v.impact ?? ''))).toEqual([])

  await mockPortalApi(page, 'User')
  await page.goto('/calendar')
  await expect(page.getByRole('main')).toBeVisible()
  results = await new AxeBuilder({ page }).analyze()
  expect(results.violations.filter(v => ['critical', 'serious'].includes(v.impact ?? ''))).toEqual([])
})
```

Tab from the skip link to main content; verify focused date button opens only a read-only interaction for User and Enter/Space behavior is native.

- [ ] **Step 5: Add responsive and authorization checks**

For widths `[375, 768, 1024, 1440]`, assert `document.documentElement.scrollWidth <= window.innerWidth`. Measure every visible icon action button and assert width/height at least 44. Run `page.emulateMedia({ reducedMotion: 'reduce' })` and assert computed CSS animation duration is no more than `0.01ms` for animated login decorations.

Authorization tests assert `新增事件`, `新增成員`, `新增行程`, `編輯`, and `刪除` are absent for User and visible for Admin, while read data remains visible for both.

- [ ] **Step 6: Verify and commit**

```bash
cd frontend
pnpm test:e2e
git add package.json pnpm-lock.yaml playwright.config.ts e2e
git commit -m "test: add browser accessibility regression suite"
```

Expected: Chromium suite passes at all viewports with no serious axe violations.

### Task 2: Run Full Local Verification and Secret Scan

**Files:**
- Create: `scripts/verify.sh`

**Interfaces:**
- Produces: one non-destructive verification entry point; skips unavailable runtimes with a clear non-zero-required CI note rather than claiming success.

- [ ] **Step 1: Create the verification script**

```bash
#!/usr/bin/env bash
set -euo pipefail

cd frontend
pnpm audit:prod
pnpm lint
pnpm test
pnpm build
pnpm test:e2e
cd ..

if command -v dotnet >/dev/null 2>&1; then
  dotnet build backend/FamilyPortal.slnx --configuration Release
  dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --configuration Release
else
  echo '.NET 本機不可用；必須以 GitHub Actions backend job 補驗。'
fi

if rg -n --hidden --glob '!.git/**' --glob '!frontend/node_modules/**' \
  '(BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY|JWT_SECRET_KEY=[^r]|MONGO_APP_PASSWORD=[^r]|ADMIN_PASSWORD=[^r])' .; then
  echo '偵測到疑似 secret。' >&2
  exit 1
fi
```

- [ ] **Step 2: Execute the script**

```bash
chmod +x scripts/verify.sh
./scripts/verify.sh
```

Expected: available checks pass. Missing .NET is explicitly recorded for remote verification; no MongoDB command runs.

- [ ] **Step 3: Commit**

```bash
git add scripts/verify.sh
git commit -m "test: add comprehensive verification entrypoint"
```

### Task 3: Add Playwright to CI and Verify Remote Gates

**Files:**
- Modify: `.github/workflows/ci.yml`

**Interfaces:**
- Produces: `frontend-e2e` job and failure-only Playwright artifact.

- [ ] **Step 1: Add the CI job**

```yaml
frontend-e2e:
  name: Frontend E2E
  runs-on: ubuntu-latest
  steps:
    - uses: actions/checkout@v5
    - uses: actions/setup-node@v5
      with:
        node-version: 22
        cache: pnpm
        cache-dependency-path: frontend/pnpm-lock.yaml
    - run: corepack enable pnpm
    - working-directory: frontend
      run: pnpm install --frozen-lockfile
    - working-directory: frontend
      run: pnpm exec playwright install --with-deps chromium
    - working-directory: frontend
      run: pnpm test:e2e
    - if: failure()
      uses: actions/upload-artifact@v4
      with:
        name: playwright-report
        path: frontend/playwright-report
        retention-days: 14
```

- [ ] **Step 2: Commit and push the verified branch**

```bash
git add .github/workflows/ci.yml
git commit -m "ci: run browser accessibility tests"
git push -u origin HEAD
```

- [ ] **Step 3: Observe GitHub Actions with GitHub CLI**

```bash
gh run list --branch "$(git branch --show-current)" --limit 5
gh run watch --exit-status
```

Expected: backend, frontend, frontend-e2e, supply-chain and CodeQL workflows complete successfully. If repository settings prevent CodeQL upload, record the exact permission blocker and keep code scanning configured; do not label that gate passed.

### Task 4: Update the Risk Register and Acceptance Evidence

**Files:**
- Modify: `docs/03-安全風險與修復台帳.md`
- Modify: `docs/04-部署與維運手冊.md`
- Modify: `docs/05-企業級成熟度與後續路線圖.md`
- Modify: `docs/07-Skills全面自檢報告.md`
- Create: `docs/08-全面修復驗收報告.md`
- Modify: `docs/README.md`

**Interfaces:**
- Produces: every audit ID linked to fix commit, test command, CI run and residual responsibility.

- [ ] **Step 1: Build the finding matrix from Git evidence**

For every ID `AUD-DEP-001` through all Low findings, add columns:

```markdown
| Finding | 修復狀態 | 程式控制 | 驗證證據 | 殘餘責任 |
|---|---|---|---|---|
| AUD-DEP-001 | 已修復 | Next.js 16.2.11 | `pnpm audit:prod`、以 `git log --oneline -- frontend/package.json` 取得的修復 commit、以 `gh run view --json url` 取得的 CI URL | 每月追蹤 Active LTS security release |
```

Use actual SHAs and URLs from `git log` and `gh run view`; angle-bracket descriptions are replaced with their retrieved values before commit.

- [ ] **Step 2: Update operational procedures**

Deployment manual must contain executable sections for:

```bash
docker compose --env-file .env.production -f docker-compose.production.yml config
docker compose --env-file .env.production -f docker-compose.production.yml up -d
curl -I https://portal.example.com
curl -I https://api.example.com/health/ready
mongodump --uri "$MONGODB_BACKUP_URI" --archive="familyportal-$(date +%F).archive" --gzip
mongorestore --uri "$MONGODB_RESTORE_TEST_URI" --archive="familyportal-YYYY-MM-DD.archive" --gzip --drop
```

The restore command is explicitly labeled **僅限隔離 restore-test database，不得對正式資料庫執行**. Include rollback to previous image digest, Secret Manager rotation, 90-day restore drill, alerting, RTO/RPO decision and incident contacts as external operational controls.

- [ ] **Step 3: Write the final acceptance report**

`08-全面修復驗收報告.md` sections are:

1. 管理結論。
2. 資料保護聲明。
3. High／Medium／Low before-after counts。
4. Dependency、backend、frontend、browser、CI command evidence。
5. Architecture diagram and trust boundaries。
6. Repository controls completed。
7. External environment controls pending。
8. Skills actually used and how each changed the work。
9. Release commit and GitHub run URLs。

- [ ] **Step 4: Validate links and commit**

```bash
git diff --check
```

Expected: no whitespace errors; manually verify every finding row contains a concrete commit SHA and CI URL obtained from the commands above.

```bash
git add docs
git commit -m "docs: record comprehensive remediation evidence"
```

### Task 5: Rebuild the Open Design Executive Presentation

**Files:**
- Modify: `docs/presentation/OPEN-DESIGN.md`
- Modify: `docs/presentation/HsuFamily-Enterprise-Readiness.pptx`
- Create: `docs/presentation/rendered/` page previews during verification; keep only final approved previews if repository size remains reasonable.

**Interfaces:**
- Produces: 10–12 slide, management-readable deck whose source and PPTX agree.

- [ ] **Step 1: Update the editable slide source**

Use this exact slide sequence:

1. 封面：許氏入口網全面修復成果。
2. 一頁結論：從「具企業思維」到「repository 控制可驗證」。
3. 原始風險：3 High、13 Medium groups、4 Low groups。
4. 修復地圖：供應鏈、部署、後端、前端、SDLC。
5. 資料保護：沒有 drop、沒有清空、API 相容。
6. 安全架構：TLS edge → frontend/API → Application Services → MongoDB。
7. 使用者成果：權限一致、錯誤可恢復、a11y、台灣農曆。
8. 工程證據：tests、audit、CodeQL、Trivy、SBOM、Playwright。
9. Before／After 成熟度雷達或條形圖，數值來源明記為本次 control checklist。
10. 尚待環境完成：TLS 實際憑證、Secret Manager、restore drill、monitoring。
11. 90 天營運路線圖。
12. 決策頁：允許 staging 驗收，正式公開仍需外部 gates 簽核。

- [ ] **Step 2: Generate the PPTX with the presentations skill**

Use the bundled presentation runtime, reuse the existing visual language, and ensure:

- body text at least 18pt, headings at least 30pt;
- no paragraph longer than 5 lines;
- charts include labels and source note;
- diagrams use consistent color semantics: green completed, amber external pending, red blocked/high;
- speaker notes explain each slide in plain Traditional Chinese.

- [ ] **Step 3: Render and visually inspect every slide**

Render PPTX to PNG previews with the presentation skill's prescribed verifier. Inspect every slide for clipping, overlap, unreadable contrast and missing fonts. Correct source and regenerate until all slides pass.

- [ ] **Step 4: Commit the presentation**

```bash
git add docs/presentation/OPEN-DESIGN.md docs/presentation/HsuFamily-Enterprise-Readiness.pptx
git commit -m "docs: update enterprise remediation briefing"
```

### Task 6: Sync the Approved Knowledge Base to Obsidian

**Files:**
- Mirror: `docs/*.md`
- Mirror: `docs/presentation/OPEN-DESIGN.md`
- Mirror: `docs/presentation/HsuFamily-Enterprise-Readiness.pptx`
- Target: `/Volumes/USB/阿豬程式資料/HsuFamily-Obsidian/許氏入口網/`

**Interfaces:**
- Produces: local Obsidian copy with the same SHA-256 content as Git source files.

- [ ] **Step 1: Request filesystem approval for the sibling vault**

The target is outside the repository writable root. Request explicit approval before writing; do not broaden the target beyond the exact `許氏入口網` directory.

- [ ] **Step 2: Mirror only approved artifacts**

```bash
rsync -av --include='*/' --include='*.md' --include='*.pptx' --exclude='*' \
  docs/ /Volumes/USB/阿豬程式資料/HsuFamily-Obsidian/許氏入口網/
```

- [ ] **Step 3: Verify source and target hashes**

```bash
find docs -type f \( -name '*.md' -o -name '*.pptx' \) -print0 | sort -z | xargs -0 shasum -a 256
find /Volumes/USB/阿豬程式資料/HsuFamily-Obsidian/許氏入口網 -type f \( -name '*.md' -o -name '*.pptx' \) -print0 | sort -z | xargs -0 shasum -a 256
```

Compare by relative path and assert every mirrored artifact hash matches. Obsidian sync itself is not committed because it is outside Git.

### Task 7: Final Release Verification and Push

**Files:**
- No new file unless verification exposes a concrete defect.

**Interfaces:**
- Produces: clean worktree, pushed commits and a final evidence-backed readiness statement.

- [ ] **Step 1: Run final verification from a clean state**

```bash
git status --short
./scripts/verify.sh
git diff --check
```

Expected: empty status before/after verification and all local checks pass.

- [ ] **Step 2: Push and wait for remote checks**

```bash
git push origin HEAD
gh run list --branch "$(git branch --show-current)" --limit 10
gh run watch --exit-status
```

Expected: all required workflows green. Record the final commit SHA and run URLs in `08-全面修復驗收報告.md`; if this modifies the report, commit, push and wait once more.

- [ ] **Step 3: State the bounded conclusion**

The final report must use this distinction:

```text
已完成：repository 內可實作與自動驗證的企業級工程控制。
尚待正式環境驗收：TLS 憑證、Secret Manager、備份還原演練、監控告警與組織審批。
```

Do not claim the second line is complete without external evidence.

## Plan Acceptance

This plan is complete only when Playwright and axe pass, local verification is recorded, GitHub CI／CodeQL／supply-chain gates are observed, every audit finding has real commit and run evidence, the PPTX has been rendered and visually inspected, Obsidian hashes match, the worktree is clean, and the pushed GitHub commit matches the documented release SHA.
