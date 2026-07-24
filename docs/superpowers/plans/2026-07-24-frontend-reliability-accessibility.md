# Frontend Reliability, Accessibility, and Culture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修正一般使用者誤見管理操作、錯誤恢復、表單標示、鍵盤／觸控／動態效果、SignalR 重同步與台灣節日／農曆資料問題。

**Architecture:** 保留 App Router 與 React Query；將純規則抽到 `lib` 便於單元測試，將重複 UX 抽成 focused components。Server Components 仍為預設，只有 store、form、query、SignalR 與互動元件使用 `use client`。

**Tech Stack:** Next.js 16.2.11、React 19、TypeScript strict、Tailwind CSS 4、shadcn/Base UI、TanStack React Query 5、Zustand 5、Zod 4、Framer Motion 12、SignalR 10、Vitest、Testing Library、Playwright、axe-core。

## Global Constraints

- 不改變 API 路徑、`ApiResponse<T>` envelope 或既有核心 JSON 欄位。
- 禁止 `any`、`dangerouslySetInnerHTML`、Web Storage token 與元件內直接 `fetch`。
- 管理員 UI gating 只改善 UX，後端 `AdminOnly` 永遠保留。
- 可觸控 control 最小 44×44px；所有表單 error 與欄位程式化關聯。
- React Query 不重試 401、403、404 或 4xx validation errors；network／5xx 最多重試 2 次。
- 農曆格式固定 `農曆X月X日`／`農曆閏X月X日`；不使用套件的地區性 festivals 作 UI 來源。
- 所有互動變更先建立失敗測試，再修改 production component。

---

## File Map

- `vitest.config.ts`／`src/test/setup.ts`：前端單元與元件測試環境。
- `src/lib/queryRetry.ts`：React Query retry policy。
- `src/lib/lunar.ts`：台灣節日 allowlist、農曆 conversion／formatting。
- `src/hooks/useSignalR.ts`：連線狀態與重連同步。
- `src/components/common/ErrorState.tsx`：可恢復錯誤 UI。
- `src/components/common/ConnectionStatus.tsx`：非侵入式即時連線狀態。
- `src/components/common/ConfirmDialog.tsx`：取代 `confirm()`。
- `src/components/layout/PortalLayout.tsx`：從 route layout 抽出的 client navigation shell。
- `src/app/(portal)/layout.tsx`：Server route wrapper。
- `src/app/(portal)/loading.tsx`／`error.tsx`：route recovery。
- 三個 portal pages：Admin gating、ErrorState、semantic controls。
- 三個 feature dialogs：Zod limits、label/error association、submit recovery。
- `src/app/globals.css`：skip link、focus、reduced motion。

### Task 1: Add Vitest and Testing Library Harness

**Files:**
- Modify: `frontend/package.json`
- Modify: `frontend/pnpm-lock.yaml`
- Create: `frontend/vitest.config.ts`
- Create: `frontend/src/test/setup.ts`
- Create: `frontend/src/lib/__tests__/smoke.test.ts`

**Interfaces:**
- Produces: `pnpm test` and `pnpm test:coverage` using jsdom and `@/` alias.

- [ ] **Step 1: Add a test script that initially fails**

Add scripts:

```json
{
  "test": "vitest run",
  "test:watch": "vitest",
  "test:coverage": "vitest run --coverage"
}
```

Run `pnpm test`; expected: failure because Vitest is not installed.

- [ ] **Step 2: Install the focused test dependencies**

```bash
cd frontend
pnpm add -D vitest @vitest/coverage-v8 jsdom @vitejs/plugin-react @testing-library/react @testing-library/jest-dom @testing-library/user-event
```

Create config:

```ts
import path from 'node:path'
import { defineConfig } from 'vitest/config'
import react from '@vitejs/plugin-react'

export default defineConfig({
  plugins: [react()],
  resolve: { alias: { '@': path.resolve(__dirname, './src') } },
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    restoreMocks: true,
    coverage: { provider: 'v8', reporter: ['text', 'lcov'] },
  },
})
```

Setup:

```ts
import '@testing-library/jest-dom/vitest'
```

Smoke test:

```ts
import { describe, expect, it } from 'vitest'

describe('test harness', () => {
  it('runs in strict TypeScript project', () => expect(document).toBeDefined())
})
```

- [ ] **Step 3: Verify and commit**

```bash
cd frontend
pnpm test
pnpm lint
git add package.json pnpm-lock.yaml vitest.config.ts src/test src/lib/__tests__/smoke.test.ts
git commit -m "test: add frontend component test harness"
```

Expected: tests and lint pass.

### Task 2: Implement Query Retry Policy and Recoverable Route Errors

**Files:**
- Create: `frontend/src/lib/queryRetry.ts`
- Create: `frontend/src/lib/__tests__/queryRetry.test.ts`
- Modify: `frontend/src/components/layout/Providers.tsx`
- Create: `frontend/src/components/common/ErrorState.tsx`
- Create: `frontend/src/components/common/__tests__/ErrorState.test.tsx`
- Create: `frontend/src/app/(portal)/loading.tsx`
- Create: `frontend/src/app/(portal)/error.tsx`

**Interfaces:**
- Produces: `shouldRetryQuery(failureCount: number, error: unknown): boolean` and reusable `ErrorState`.

- [ ] **Step 1: Write failing retry tests**

```ts
import { AxiosError } from 'axios'
import { describe, expect, it } from 'vitest'
import { shouldRetryQuery } from '@/lib/queryRetry'

function axiosStatus(status: number) {
  return new AxiosError('request failed', 'ERR_BAD_RESPONSE', undefined, undefined, {
    status, statusText: '', headers: {}, config: { headers: {} }, data: {},
  })
}

describe('shouldRetryQuery', () => {
  it.each([400, 401, 403, 404, 422])('does not retry %s', status =>
    expect(shouldRetryQuery(0, axiosStatus(status))).toBe(false))
  it('retries a 503 at most twice', () => {
    expect(shouldRetryQuery(0, axiosStatus(503))).toBe(true)
    expect(shouldRetryQuery(2, axiosStatus(503))).toBe(false)
  })
})
```

- [ ] **Step 2: Run RED and implement retry policy**

```ts
import axios from 'axios'

export function shouldRetryQuery(failureCount: number, error: unknown): boolean {
  if (failureCount >= 2) return false
  if (!axios.isAxiosError(error) || !error.response) return true
  return error.response.status >= 500
}
```

Use `retry: shouldRetryQuery` in `Providers.tsx`.

- [ ] **Step 3: Write failing ErrorState test**

```tsx
it('announces the error and offers retry', async () => {
  const retry = vi.fn()
  render(<ErrorState title="無法載入行事曆" onRetry={retry} />)
  expect(screen.getByRole('alert')).toHaveTextContent('無法載入行事曆')
  await userEvent.click(screen.getByRole('button', { name: '重新嘗試' }))
  expect(retry).toHaveBeenCalledOnce()
})
```

- [ ] **Step 4: Implement route recovery**

`ErrorState` accepts `title`, optional `description`, and optional `onRetry`; button text is `重新嘗試`. Portal `error.tsx` is a client component and renders:

```tsx
export default function PortalError({ reset }: { error: Error; reset: () => void }) {
  return <ErrorState title="頁面暫時無法載入" description="請檢查網路後再試一次。" onRetry={reset} />
}
```

`loading.tsx` renders three `Skeleton` cards with `aria-label="頁面載入中"` and `role="status"`.

- [ ] **Step 5: Verify and commit**

```bash
cd frontend
pnpm test -- src/lib/__tests__/queryRetry.test.ts src/components/common/__tests__/ErrorState.test.tsx
pnpm lint
git add src/lib/queryRetry.ts src/lib/__tests__/queryRetry.test.ts src/components/layout/Providers.tsx src/components/common src/app/'(portal)'/loading.tsx src/app/'(portal)'/error.tsx
git commit -m "fix: add recoverable frontend error handling"
```

### Task 3: Replace Regional Festival Data with Taiwan Rules

**Files:**
- Modify: `frontend/src/lib/lunar.ts`
- Create: `frontend/src/lib/__tests__/lunar.test.ts`
- Modify: `frontend/src/components/features/members/MemberDialog.tsx`

**Interfaces:**
- Produces: `formatLunarDate`、`getTaiwanSolarFestivals`、`getTaiwanLunarFestivals`; `getMonthLunarInfo` never surfaces Mainland-only festivals.

- [ ] **Step 1: Write cultural and boundary tests**

```ts
describe('Taiwan calendar rules', () => {
  it('does not label July 1 as 建党节', () => {
    const day = getMonthLunarInfo(2026, 7).find(value => value.solarDate === '2026-07-01')
    expect(day?.solarFestivals).not.toContain('建党节')
  })

  it('formats regular and leap lunar dates', () => {
    expect(formatLunarDate(1, 1, false)).toBe('農曆正月初一')
    expect(formatLunarDate(4, 15, true)).toBe('農曆閏四月十五')
  })

  it.each([[0, 1], [13, 1], [1, 0], [1, 31]])('rejects invalid lunar %s/%s', (month, day) => {
    expect(() => formatLunarDate(month, day, false)).toThrow('農曆日期不合法')
  })
})
```

- [ ] **Step 2: Run RED and implement explicit maps**

```ts
const taiwanSolarFestivals: Record<string, string[]> = {
  '01-01': ['元旦'],
  '02-28': ['和平紀念日'],
  '04-04': ['兒童節'],
  '05-01': ['勞動節'],
  '10-10': ['國慶日'],
  '12-25': ['行憲紀念日'],
}

const taiwanLunarFestivals: Record<string, string[]> = {
  '01-01': ['春節'],
  '01-15': ['元宵節'],
  '05-05': ['端午節'],
  '07-15': ['中元節'],
  '08-15': ['中秋節'],
  '09-09': ['重陽節'],
}
```

Export pure functions returning cloned arrays. `getMonthLunarInfo` uses these maps and keeps `lunar.getJieQi()` for 清明／冬至. It must not call `solar.getFestivals()` or `lunar.getFestivals()`.

Implement:

```ts
export function formatLunarDate(month: number, day: number, isLeapMonth = false): string {
  if (!Number.isInteger(month) || month < 1 || month > 12 ||
      !Number.isInteger(day) || day < 1 || day > 30) throw new Error('農曆日期不合法')
  return `農曆${isLeapMonth ? '閏' : ''}${lunarMonths[month - 1]}月${lunarDays[day - 1]}`
}
```

- [ ] **Step 3: Generate valid member lunar payloads**

Member schema uses `z.number().int().min(1).max(12).nullable()` and day max 30. Payload uses the selected Gregorian birthday year when present, otherwise the current year, and:

```ts
displayText: formatLunarDate(month, day, data.isLunarLeapMonth ?? false)
```

It never sends `year: 0` or an empty display string.

- [ ] **Step 4: Verify and commit**

```bash
cd frontend
pnpm test -- src/lib/__tests__/lunar.test.ts
pnpm lint
git add src/lib/lunar.ts src/lib/__tests__/lunar.test.ts src/components/features/members/MemberDialog.tsx
git commit -m "fix: use validated taiwan lunar calendar rules"
```

### Task 4: Gate Admin Actions and Add Accessible Confirmation

**Files:**
- Create: `frontend/src/components/common/AdminOnly.tsx`
- Create: `frontend/src/components/common/ConfirmDialog.tsx`
- Create: `frontend/src/components/common/__tests__/AdminOnly.test.tsx`
- Create: `frontend/src/components/common/__tests__/ConfirmDialog.test.tsx`
- Modify: `frontend/src/app/(portal)/calendar/page.tsx`
- Modify: `frontend/src/app/(portal)/members/page.tsx`
- Modify: `frontend/src/app/(portal)/itineraries/page.tsx`

**Interfaces:**
- Produces: `AdminOnly` and keyboard-accessible destructive confirmation; all create/edit/delete controls hidden for normal users.

- [ ] **Step 1: Write failing authorization UX tests**

```tsx
it('does not render children for a normal user', () => {
  useAuthStore.setState({ isAdmin: false })
  render(<AdminOnly><button>新增</button></AdminOnly>)
  expect(screen.queryByRole('button', { name: '新增' })).not.toBeInTheDocument()
})

it('renders children for an administrator', () => {
  useAuthStore.setState({ isAdmin: true })
  render(<AdminOnly><button>新增</button></AdminOnly>)
  expect(screen.getByRole('button', { name: '新增' })).toBeVisible()
})
```

- [ ] **Step 2: Implement gating**

```tsx
'use client'
import { useAuthStore } from '@/stores/authStore'

export default function AdminOnly({ children }: { children: React.ReactNode }) {
  return useAuthStore(state => state.isAdmin) ? children : null
}
```

Wrap every create, edit and delete trigger in the three pages. Keep read-only cards clickable only where a non-admin detail view is useful; do not open an editable form for normal users.

- [ ] **Step 3: Replace browser confirm**

`ConfirmDialog` props:

```ts
interface ConfirmDialogProps {
  trigger: React.ReactElement
  title: string
  description: string
  confirmLabel?: string
  onConfirm: () => Promise<void> | void
}
```

Use existing Dialog primitives, initial focus on cancel, `aria-describedby`, disabled pending state, and destructive confirm label default `確認刪除`. Test Escape cancellation and confirm callback.

- [ ] **Step 4: Verify no ungated destructive path**

```bash
cd frontend
pnpm test -- src/components/common/__tests__/AdminOnly.test.tsx src/components/common/__tests__/ConfirmDialog.test.tsx
rg -n 'window\.confirm|\bconfirm\(' src
pnpm lint
```

Expected: tests pass and `rg` finds no browser confirm.

- [ ] **Step 5: Commit**

```bash
git add src/components/common src/app/'(portal)'/calendar/page.tsx src/app/'(portal)'/members/page.tsx src/app/'(portal)'/itineraries/page.tsx
git commit -m "fix: align admin actions with authorization"
```

### Task 5: Correct Navigation, Calendar Semantics and Touch Targets

**Files:**
- Create: `frontend/src/components/layout/PortalLayout.tsx`
- Modify: `frontend/src/app/(portal)/layout.tsx`
- Modify: `frontend/src/app/(portal)/calendar/page.tsx`
- Modify: `frontend/src/app/globals.css`
- Create: `frontend/src/components/layout/__tests__/PortalLayout.test.tsx`
- Create: `frontend/src/app/(portal)/calendar/__tests__/CalendarPage.test.tsx`

**Interfaces:**
- Produces: skip link, labeled nav, `aria-current`, semantic date controls, Lucide icons and 44px targets.

- [ ] **Step 1: Write failing accessibility assertions**

```tsx
expect(screen.getByRole('link', { name: '跳到主要內容' })).toHaveAttribute('href', '#main-content')
expect(screen.getByRole('navigation', { name: '主要導覽' })).toBeVisible()
expect(screen.getByRole('link', { name: '行事曆' })).toHaveAttribute('aria-current', 'page')
expect(screen.getByRole('main')).toHaveAttribute('id', 'main-content')
```

Calendar test finds a button by full date accessible name, for example `2026年7月1日，農曆五月十七`.

- [ ] **Step 2: Extract client shell and add semantics**

Route layout remains a Server Component and returns `<PortalLayout>{children}</PortalLayout>`. Client shell uses `usePathname` and:

```tsx
<a className="skip-link" href="#main-content">跳到主要內容</a>
<nav aria-label="主要導覽">...</nav>
<Link aria-current={pathname === item.href ? 'page' : undefined} ... />
<main id="main-content" tabIndex={-1}>{children}</main>
```

Use Lucide `CalendarDays`, `Users`, `MapPinned`, `Menu`, `Plus`, `ChevronLeft`, `ChevronRight`, `Trash2`, `LogOut`, and `ShieldCheck`. Decorative icons get `aria-hidden="true"`.

- [ ] **Step 3: Make calendar days real buttons**

Each day trigger is:

```tsx
<button
  type="button"
  aria-label={`${format(day, 'yyyy年M月d日')}，${lunarInfo.displayText}`}
  className="min-h-24 w-full rounded-lg p-2 text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
>
  ...
</button>
```

Admin users may wrap it in `EventDialog`; normal users receive a non-editing button or article depending on whether detail interaction exists. Month controls and all icon buttons use `size="icon"` plus `className="size-11"` and explicit `aria-label`.

- [ ] **Step 4: Add reduced-motion and skip-link CSS**

```css
.skip-link {
  position: fixed;
  left: 1rem;
  top: 1rem;
  z-index: 100;
  transform: translateY(-200%);
}
.skip-link:focus { transform: translateY(0); }

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    scroll-behavior: auto !important;
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
  }
}
```

Wrap children under `MotionConfig reducedMotion="user"` in Providers.

- [ ] **Step 5: Verify and commit**

```bash
cd frontend
pnpm test -- src/components/layout/__tests__/PortalLayout.test.tsx src/app/'(portal)'/calendar/__tests__/CalendarPage.test.tsx
pnpm lint
git add src/components/layout src/app/'(portal)'/layout.tsx src/app/'(portal)'/calendar/page.tsx src/app/globals.css src/components/layout/Providers.tsx
git commit -m "fix: improve portal accessibility and touch targets"
```

### Task 6: Associate Form Labels, Errors and Constraints

**Files:**
- Modify: `frontend/src/app/(auth)/login/page.tsx`
- Modify: `frontend/src/components/features/calendar/EventDialog.tsx`
- Modify: `frontend/src/components/features/members/MemberDialog.tsx`
- Modify: `frontend/src/components/features/itineraries/ItineraryDialog.tsx`
- Create: `frontend/src/components/features/__tests__/FormsAccessibility.test.tsx`

**Interfaces:**
- Produces: bounded Zod schemas aligned with backend and accessible form errors.

- [ ] **Step 1: Write failing form tests**

For every dialog, open it and assert `getByLabelText` returns title/name/date inputs. Submit empty fields and assert:

```tsx
expect(screen.getByLabelText('標題')).toHaveAttribute('aria-invalid', 'true')
expect(screen.getByLabelText('標題')).toHaveAttribute('aria-describedby', 'event-title-error')
expect(screen.getByRole('alert')).toHaveTextContent('請輸入事件標題')
```

Login test asserts `autocomplete="username"` and `autocomplete="current-password"`.

- [ ] **Step 2: Align Zod limits**

Use these exact maxima:

```ts
const title = z.string().trim().min(1, '請輸入標題').max(200, '最多 200 個字元')
const description = z.string().max(4000, '最多 4000 個字元').optional()
const shortText = z.string().max(200, '最多 200 個字元').optional()
```

Member month is 1–12, day 1–30, bio max 4000; itinerary location max 200 and description max 4000. Add `maxLength` HTML attributes matching Zod.

- [ ] **Step 3: Link labels and errors**

Each field follows this exact pattern:

```tsx
<Label htmlFor="event-title">標題</Label>
<Input
  id="event-title"
  aria-invalid={Boolean(errors.title)}
  aria-describedby={errors.title ? 'event-title-error' : undefined}
  maxLength={200}
  {...register('title')}
/>
{errors.title && <p id="event-title-error" role="alert">{errors.title.message}</p>}
```

Mutation rejection leaves the dialog open and adds a form-level `role="alert"` message. The submit handler catches errors after React Query has already displayed supplementary toast feedback.

- [ ] **Step 4: Verify and commit**

```bash
cd frontend
pnpm test -- src/components/features/__tests__/FormsAccessibility.test.tsx
pnpm lint
git add src/app/'(auth)'/login/page.tsx src/components/features
git commit -m "fix: make forms bounded and accessible"
```

### Task 7: Add SignalR State and Reconnection Resynchronization

**Files:**
- Modify: `frontend/src/hooks/useSignalR.ts`
- Create: `frontend/src/hooks/__tests__/useSignalR.test.tsx`
- Create: `frontend/src/components/common/ConnectionStatus.tsx`
- Modify: `frontend/src/components/layout/PortalLayout.tsx`

**Interfaces:**
- Produces: `SignalRStatus` union and `{ on, status, reconnect }`; reconnected sessions invalidate calendar and itinerary queries.

- [ ] **Step 1: Write failing hook tests with a mocked HubConnectionBuilder**

```ts
it('shows reconnecting and invalidates stale queries after reconnection', async () => {
  const { result } = renderHook(() => useSignalR(), { wrapper })
  act(() => fakeConnection.fireReconnecting())
  expect(result.current.status).toBe('reconnecting')
  await act(() => fakeConnection.fireReconnected())
  expect(result.current.status).toBe('connected')
  expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['calendar-events'] })
  expect(invalidateQueries).toHaveBeenCalledWith({ queryKey: ['itineraries'] })
})
```

- [ ] **Step 2: Implement explicit state without token logging**

```ts
export type SignalRStatus = 'idle' | 'connecting' | 'connected' | 'reconnecting' | 'disconnected'
```

Use `useQueryClient`; set status in `start`, `onreconnecting`, `onreconnected`, and `onclose`. On reconnected:

```ts
await Promise.all([
  queryClient.invalidateQueries({ queryKey: ['calendar-events'] }),
  queryClient.invalidateQueries({ queryKey: ['itineraries'] }),
])
```

Expose a manual reconnect that reuses the same connection and does not create duplicate handlers:

```ts
const reconnect = useCallback(async () => {
  const connection = connectionRef.current
  if (!connection || connection.state !== signalR.HubConnectionState.Disconnected) return
  setStatus('connecting')
  try {
    await connection.start()
    setStatus('connected')
    await Promise.all([
      queryClient.invalidateQueries({ queryKey: ['calendar-events'] }),
      queryClient.invalidateQueries({ queryKey: ['itineraries'] }),
    ])
  } catch {
    setStatus('disconnected')
  }
}, [queryClient])
```

Track `isMounted` and never call state setters after cleanup. Remove success `console.log`; log failures only with a fixed message and no token／URL query data.

- [ ] **Step 3: Add visible connection status**

`ConnectionStatus` renders nothing when connected/idle. For reconnecting it renders `即時更新重新連線中` with `role="status"`; disconnected renders `即時更新已中斷` and a `重新連線` button.

- [ ] **Step 4: Verify and commit**

```bash
cd frontend
pnpm test -- src/hooks/__tests__/useSignalR.test.tsx
pnpm lint
git add src/hooks/useSignalR.ts src/hooks/__tests__/useSignalR.test.tsx src/components/common/ConnectionStatus.tsx src/components/layout/PortalLayout.tsx
git commit -m "fix: resynchronize realtime data after reconnect"
```

### Task 8: Complete Frontend Regression Verification

**Files:**
- Modify: portal pages and hooks only if failing regression identifies a concrete defect.

**Interfaces:**
- Consumes: all preceding frontend tasks.
- Produces: green unit suite, lint and production build.

- [ ] **Step 1: Run the full local suite**

```bash
cd frontend
pnpm test:coverage
pnpm lint
pnpm build
pnpm audit:prod
```

Expected: all commands exit 0; no High audit finding; tests cover retry policy, admin gating, lunar allowlist, forms and SignalR state.

- [ ] **Step 2: Scan forbidden patterns**

```bash
rg -n 'dangerouslySetInnerHTML|:\s*any\b|window\.confirm|localStorage|sessionStorage|getFestivals\(' src
```

Expected: no matches. If a test fixture intentionally contains a forbidden word, constrain the scan to production paths or assert the fixture is not executable code.

- [ ] **Step 3: Commit only concrete verification fixes**

If verification required production changes, stage the exact changed files and use:

```bash
git commit -m "fix: resolve frontend regression findings"
```

If no change is required, do not create an empty commit.

## Plan Acceptance

The frontend is accepted when unit tests, lint, build and audit pass; normal users cannot discover CRUD controls; every named form control has an associated label and error; calendar actions are semantic 44px controls; reduced-motion suppresses animation; SignalR reconnection invalidates stale queries; and July 1 never displays `建党节`.
