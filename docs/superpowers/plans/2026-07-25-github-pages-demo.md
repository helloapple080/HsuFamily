# GitHub Pages Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立不連接 API／MongoDB／SignalR、可由 GitHub Pages 免費公開瀏覽的許氏入口網唯讀展示版。

**Architecture:** 保留正式版 Next.js standalone 行為，以 `NEXT_PUBLIC_DEMO_MODE=true` 在 build time 切換 typed fixtures 與唯讀互動。Demo build 暫時停用 static export 不支援的 `proxy.ts`，使用 `/HsuFamily` base path 輸出至 `frontend/out`，再由 GitHub Actions Pages workflow 發布。

**Tech Stack:** Next.js 16.2.11、React 19、TypeScript 5、TanStack React Query 5、Vitest 4、GitHub Actions／Pages。

## Global Constraints

- Demo URL 固定為 `https://helloapple080.github.io/HsuFamily/`。
- Demo fixtures 全為虛構資料，禁止讀取或複製 MongoDB 內容。
- Demo 模式禁止 API、Cookie、CSRF、SignalR 與持久化寫入。
- 正式模式維持 `output: "standalone"` 及既有安全流程。
- Demo 模式使用 `output: "export"`、`basePath: "/HsuFamily"`、`trailingSlash: true`、`images.unoptimized: true`。
- UI 文字使用繁體中文，持續顯示「公開展示模式」。
- PR 僅驗證；只有 `main` 可部署 Pages。

---

## File Structure

- Create `frontend/src/lib/demo/config.ts`: 唯一 Demo mode 判斷與公開 base path。
- Create `frontend/src/lib/demo/fixtures.ts`: typed、deep-frozen 的虛構事件／成員／行程。
- Create `frontend/src/lib/demo/actions.ts`: 統一唯讀 mutation rejection 與提示訊息。
- Create `frontend/src/components/common/DemoModeBanner.tsx`: 全站展示標記。
- Create `frontend/scripts/build-pages.mjs`: 安全暫停 `proxy.ts`、執行 static export、finally 還原。
- Create `frontend/scripts/assert-pages-output.mjs`: 檢查輸出路由與秘密字串。
- Create `.github/workflows/pages.yml`: Pages build／deploy。
- Modify `frontend/next.config.ts`: 雙模式輸出設定。
- Modify `frontend/package.json`: `build:pages` 與 `test:pages` scripts。
- Modify `frontend/src/app/page.tsx`: Demo 根頁靜態入口。
- Modify `frontend/src/components/layout/Providers.tsx`: Demo user bootstrap，不呼叫 `/api/auth/me`。
- Modify `frontend/src/components/layout/PortalLayout.tsx`: banner、Demo logout、連線文案。
- Modify `frontend/src/hooks/useCalendarEvents.ts`, `useMembers.ts`, `useItineraries.ts`: query 使用 fixtures，mutation 統一唯讀拒絕。
- Modify `frontend/src/hooks/useSignalR.ts`: Demo 模式不建立 HubConnection。
- Add colocated Vitest tests for config、fixtures、hooks、banner、Providers 與 SignalR。
- Modify `docs/README.md`: 公開 Demo URL、限制與部署證據。

---

### Task 1: Dual-mode static export boundary

**Files:**
- Create: `frontend/src/lib/demo/config.ts`
- Create: `frontend/src/lib/demo/__tests__/config.test.ts`
- Create: `frontend/scripts/build-pages.mjs`
- Create: `frontend/scripts/assert-pages-output.mjs`
- Modify: `frontend/next.config.ts`
- Modify: `frontend/package.json`

**Interfaces:**
- Produces: `isDemoMode: boolean`, `demoBasePath: "/HsuFamily" | ""`, scripts `pnpm build:pages` and `pnpm test:pages`.
- Consumes: `NEXT_PUBLIC_DEMO_MODE` and `GITHUB_ACTIONS` build-time environment values.

- [ ] **Step 1: Write the failing mode test**

```ts
import { afterEach, describe, expect, it, vi } from 'vitest'

describe('demo config', () => {
  afterEach(() => vi.unstubAllEnvs())

  it('只在明確設為 true 時啟用展示模式', async () => {
    vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'true')
    vi.resetModules()
    const config = await import('../config')
    expect(config.isDemoMode).toBe(true)
    expect(config.demoBasePath).toBe('/HsuFamily')
  })
})
```

- [ ] **Step 2: Run the test and confirm the missing module failure**

Run: `cd frontend && pnpm test -- src/lib/demo/__tests__/config.test.ts`

Expected: FAIL because `../config` does not exist.

- [ ] **Step 3: Implement the mode boundary and dual Next config**

```ts
// frontend/src/lib/demo/config.ts
export const isDemoMode = process.env.NEXT_PUBLIC_DEMO_MODE === 'true'
export const demoBasePath = isDemoMode ? '/HsuFamily' : ''
export const demoReadOnlyMessage = '公開展示模式不會儲存任何變更。'
```

```ts
// frontend/next.config.ts core shape
const isDemoMode = process.env.NEXT_PUBLIC_DEMO_MODE === 'true'
const nextConfig: NextConfig = isDemoMode
  ? { output: 'export', basePath: '/HsuFamily', trailingSlash: true, images: { unoptimized: true }, poweredByHeader: false }
  : { output: 'standalone', poweredByHeader: false }
```

`build-pages.mjs` must rename `src/proxy.ts` to `src/proxy.ts.demo-disabled`, spawn `next build` with `NEXT_PUBLIC_DEMO_MODE=true`, and restore the file in a `finally` block even when the build fails. It must abort before build if either both proxy paths exist or neither exists. `assert-pages-output.mjs` must require `out/index.html`, `out/calendar/index.html`, `out/members/index.html`, and `out/itineraries/index.html`, recursively scan text assets, and fail on `mongodb://`, `mongodb+srv://`, `Jwt__Secret`, or `/api/auth/login`.

- [ ] **Step 4: Add package scripts and verify both builds**

```json
{
  "build:pages": "node scripts/build-pages.mjs",
  "test:pages": "node scripts/assert-pages-output.mjs"
}
```

Run: `cd frontend && pnpm test -- src/lib/demo/__tests__/config.test.ts && pnpm build && pnpm build:pages && pnpm test:pages`

Expected: config test PASS; standalone build PASS; static build emits `out`; output assertion PASS; `src/proxy.ts` exists afterward.

- [ ] **Step 5: Commit**

```bash
git add frontend/next.config.ts frontend/package.json frontend/src/lib/demo/config.ts frontend/src/lib/demo/__tests__/config.test.ts frontend/scripts/build-pages.mjs frontend/scripts/assert-pages-output.mjs
git commit -m "feat: add isolated github pages build"
```

---

### Task 2: Typed read-only Demo data adapter

**Files:**
- Create: `frontend/src/lib/demo/fixtures.ts`
- Create: `frontend/src/lib/demo/actions.ts`
- Create: `frontend/src/lib/demo/__tests__/fixtures.test.ts`
- Modify: `frontend/src/hooks/useCalendarEvents.ts`
- Modify: `frontend/src/hooks/useMembers.ts`
- Modify: `frontend/src/hooks/useItineraries.ts`
- Test: `frontend/src/hooks/__tests__/demoDataHooks.test.tsx`

**Interfaces:**
- Produces: `demoEvents: readonly CalendarEvent[]`, `demoMembers: readonly Member[]`, `demoItineraries: readonly Itinerary[]`, `rejectDemoWrite(): Promise<never>`.
- Consumes: existing `CalendarEvent`, `Member`, `Itinerary`, request DTOs and React Query hook return contracts.

- [ ] **Step 1: Write failing fixture and hook tests**

```ts
it('fixtures 不含真實管理員名稱且為唯讀', () => {
  expect(JSON.stringify({ demoEvents, demoMembers, demoItineraries })).not.toContain('許浴宸')
  expect(Object.isFrozen(demoMembers)).toBe(true)
})

it('展示模式讀取成員時不呼叫 API', async () => {
  vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'true')
  const { result } = renderHook(() => useMembers(), { wrapper })
  await waitFor(() => expect(result.current.isSuccess).toBe(true))
  expect(api.get).not.toHaveBeenCalled()
  expect(result.current.data).toHaveLength(4)
})
```

- [ ] **Step 2: Run focused tests and confirm failure**

Run: `cd frontend && pnpm test -- src/lib/demo/__tests__/fixtures.test.ts src/hooks/__tests__/demoDataHooks.test.tsx`

Expected: FAIL because fixtures and Demo branches do not exist.

- [ ] **Step 3: Add fictional fixtures and write rejection**

Fixtures must use clearly fictional labels such as `示範長輩`、`示範成員甲`、`demo-member@example.invalid` and `0900-000-000`. Freeze the top-level arrays and every object.

```ts
export async function rejectDemoWrite(): Promise<never> {
  throw new Error(demoReadOnlyMessage)
}
```

- [ ] **Step 4: Branch each query and mutation at its transport boundary**

```ts
queryFn: async () => {
  if (isDemoMode) return [...demoMembers]
  const response = await api.get<ApiResponse<Member[]>>('/api/members')
  return response.data.data ?? []
}
```

Every create／update／delete `mutationFn` begins with `if (isDemoMode) return rejectDemoWrite()`. It must not invalidate or optimistically change fixtures after rejection. Itinerary search and pagination filter／slice the in-memory fixtures and preserve the existing `useInfiniteQuery` page shape.

- [ ] **Step 5: Run focused and complete regression tests**

Run: `cd frontend && pnpm test -- src/lib/demo/__tests__/fixtures.test.ts src/hooks/__tests__/demoDataHooks.test.tsx && pnpm test`

Expected: focused tests PASS; complete suite includes all previous 54 tests and new tests with zero failures.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/lib/demo frontend/src/hooks/useCalendarEvents.ts frontend/src/hooks/useMembers.ts frontend/src/hooks/useItineraries.ts frontend/src/hooks/__tests__/demoDataHooks.test.tsx
git commit -m "feat: add read-only demo data adapters"
```

---

### Task 3: Public Demo shell and no-network guarantees

**Files:**
- Create: `frontend/src/components/common/DemoModeBanner.tsx`
- Create: `frontend/src/components/common/__tests__/DemoModeBanner.test.tsx`
- Modify: `frontend/src/app/page.tsx`
- Modify: `frontend/src/components/layout/Providers.tsx`
- Modify: `frontend/src/components/layout/PortalLayout.tsx`
- Modify: `frontend/src/components/common/ConnectionStatus.tsx`
- Modify: `frontend/src/hooks/useSignalR.ts`
- Modify: `frontend/src/components/layout/__tests__/PortalLayout.test.tsx`
- Modify: `frontend/src/hooks/__tests__/useSignalR.test.tsx`

**Interfaces:**
- Produces: `DemoModeBanner`, direct root navigation, a fictional non-admin viewer, `SignalRStatus` presentation equivalent to `展示資料` without HubConnection creation.
- Consumes: `isDemoMode`, `demoReadOnlyMessage`, existing auth store and Portal layout.

- [ ] **Step 1: Write failing UI and transport tests**

```tsx
it('顯示持續可見的公開展示標記', () => {
  render(<DemoModeBanner />)
  expect(screen.getByText('公開展示模式')).toBeVisible()
  expect(screen.getByText(/不會儲存/)).toBeVisible()
})

it('展示模式不建立 SignalR connection', async () => {
  vi.stubEnv('NEXT_PUBLIC_DEMO_MODE', 'true')
  const { result } = renderHook(() => useSignalR(), { wrapper })
  expect(HubConnectionBuilder).not.toHaveBeenCalled()
  expect(result.current.status).toBe('demo')
})
```

- [ ] **Step 2: Run focused tests and confirm failure**

Run: `cd frontend && pnpm test -- src/components/common/__tests__/DemoModeBanner.test.tsx src/hooks/__tests__/useSignalR.test.tsx`

Expected: FAIL because banner and `demo` status do not exist.

- [ ] **Step 3: Implement the shell behavior**

`DemoModeBanner` uses semantic `role="status"` and the exact copy `公開展示模式｜資料皆為虛構內容，操作不會儲存。`. In Demo mode, `Providers` seeds `{ id: 'demo-viewer', username: 'demo', displayName: '展示訪客', role: 'Member' }` without `api.get('/api/auth/me')`. Root page renders a static landing section with `Link href="/calendar"`; formal mode retains `redirect('/calendar')`.

`PortalLayout` renders the banner above content. Demo logout routes to `/calendar` without API calls. `useSignalR` returns stable no-op `on`／`reconnect` functions and status `demo` before creating the shared connection. `ConnectionStatus` maps `demo` to `展示資料` and must not offer reconnect.

- [ ] **Step 4: Verify UI regression and static export**

Run: `cd frontend && pnpm test && pnpm lint && pnpm build:pages && pnpm test:pages`

Expected: all tests PASS; lint PASS; static export and no-secret/no-API assertions PASS.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/app/page.tsx frontend/src/components/common/DemoModeBanner.tsx frontend/src/components/common/__tests__/DemoModeBanner.test.tsx frontend/src/components/common/ConnectionStatus.tsx frontend/src/components/layout/Providers.tsx frontend/src/components/layout/PortalLayout.tsx frontend/src/components/layout/__tests__/PortalLayout.test.tsx frontend/src/hooks/useSignalR.ts frontend/src/hooks/__tests__/useSignalR.test.tsx
git commit -m "feat: add public read-only demo shell"
```

---

### Task 4: GitHub Pages delivery and end-to-end verification

**Files:**
- Create: `.github/workflows/pages.yml`
- Modify: `docs/README.md`
- Modify: `docs/04-部署與維運手冊.md`

**Interfaces:**
- Produces: Pages artifact from `frontend/out`, production environment `github-pages`, public deployment URL.
- Consumes: `pnpm build:pages`, `pnpm test:pages`, GitHub Pages OIDC deployment.

- [ ] **Step 1: Add the least-privilege workflow**

```yaml
name: GitHub Pages Demo
on:
  pull_request:
    branches: [main]
  push:
    branches: [main]
permissions:
  contents: read
concurrency:
  group: pages
  cancel-in-progress: true
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v5
      - uses: actions/setup-node@v5
        with:
          node-version: 22
      - run: corepack enable pnpm
      - run: pnpm install --frozen-lockfile --ignore-scripts
        working-directory: frontend
      - run: pnpm test
        working-directory: frontend
      - run: pnpm lint
        working-directory: frontend
      - run: pnpm build:pages
        working-directory: frontend
      - run: pnpm test:pages
        working-directory: frontend
      - uses: actions/configure-pages@v5
        if: github.event_name == 'push'
      - uses: actions/upload-pages-artifact@v3
        if: github.event_name == 'push'
        with:
          path: frontend/out
  deploy:
    if: github.event_name == 'push'
    needs: build
    permissions:
      pages: write
      id-token: write
    environment:
      name: github-pages
      url: ${{ steps.deployment.outputs.page_url }}
    runs-on: ubuntu-latest
    steps:
      - id: deployment
        uses: actions/deploy-pages@v4
```

- [ ] **Step 2: Document the public boundary**

Add the public URL, exact Demo limitations, deployment workflow, rollback through Git revert, and explicit statement that no MongoDB data is published.

- [ ] **Step 3: Run final local verification**

Run: `cd frontend && pnpm test && pnpm lint && pnpm build && pnpm build:pages && pnpm test:pages`

Expected: all commands PASS; formal build remains standalone; Demo output contains the four routes.

- [ ] **Step 4: Browser acceptance**

Serve `frontend/out` under a local `/HsuFamily/` prefix. Check desktop 1440×900 and mobile 390×844 for `/`, `/calendar/`, `/members/`, and `/itineraries/`. Expected: no horizontal overflow, all assets load, banner remains visible, navigation succeeds, no API／SignalR request occurs, and console contains no error.

- [ ] **Step 5: Commit and publish**

```bash
git add .github/workflows/pages.yml docs/README.md docs/04-部署與維運手冊.md
git commit -m "ci: publish read-only github pages demo"
git push -u origin codex/github-pages-demo
```

Create a PR to `main`, require existing CI／CodeQL and the Pages build job, merge only when all checks pass. After merge, verify the Pages deployment job and HTTPS 200 at `https://helloapple080.github.io/HsuFamily/`.
