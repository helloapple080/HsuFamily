# Theme and Lunar Recurrence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 提供淺色／深色／系統主題，並讓固定農曆拜拜規則能在任意查詢年份動態產生正確國曆事件。

**Architecture:** 主題完全留在前端 semantic tokens。農曆規則保存在既有 Event，Application 透過 `ILunarRecurrenceService` 展開 occurrence，Infrastructure 實作換算，Repository 只負責取得一次性事件與有效規則。

**Tech Stack:** .NET 10、MongoDB Driver 3.7、Next.js 16、React 19、TypeScript 5、next-themes、lunar-javascript、xUnit、Vitest。

## Global Constraints

- 維持既有 Clean Architecture 依賴方向與 `ApiResponse<T>`。
- 所有日期使用 UTC；農曆顯示格式為 `農曆X月X日` 或 `農曆閏X月X日`。
- occurrence 不寫回 MongoDB，識別碼由規則 ID 與國曆日期穩定產生。
- 農曆三十日不存在時略過，不改成二十九日。
- 第一版編輯／刪除作用於整條規則，不提供單年例外。
- 前端 API 資料由 React Query hooks 取得，元件不得直接 `fetch`。

---

## File Map

- `backend/FamilyPortal.Domain/Enums/LeapMonthPolicy.cs`：閏月策略。
- `backend/FamilyPortal.Domain/ValueObjects/RecurrenceRule.cs`：持久化規則有效期與閏月策略。
- `backend/FamilyPortal.Application/Models/LunarOccurrence.cs`：Application 內部展開結果。
- `backend/FamilyPortal.Application/Interfaces/ILunarRecurrenceService.cs`：農曆換算邊界。
- `backend/FamilyPortal.Infrastructure/Calendar/LunarRecurrenceService.cs`：換算實作。
- `backend/FamilyPortal.Application/Services/EventService.cs`：合併一次性事件與 occurrence。
- `frontend/src/components/theme/ThemeSwitcher.tsx`：三態主題控制。
- `frontend/src/components/features/calendar/EventDialog.tsx`：農曆規則表單。

### Task 1: Theme switcher

**Files:**
- Create: `frontend/src/components/theme/ThemeSwitcher.tsx`
- Create: `frontend/src/components/theme/__tests__/ThemeSwitcher.test.tsx`
- Modify: `frontend/src/components/layout/Providers.tsx`
- Modify: `frontend/src/components/layout/PortalLayout.tsx`
- Test: `frontend/src/components/layout/__tests__/PortalLayout.test.tsx`

**Interfaces:**
- Consumes: `useTheme(): { theme, setTheme }` from `next-themes`.
- Produces: `export default function ThemeSwitcher()` with `light | dark | system` actions.

- [ ] **Step 1: Write failing theme tests**

```tsx
it('offers light, dark and system themes', async () => {
  render(<ThemeSwitcher />)
  expect(screen.getByRole('button', { name: '切換顯示主題' })).toBeInTheDocument()
  await user.click(screen.getByRole('button', { name: '切換顯示主題' }))
  expect(screen.getByText('淺色')).toBeInTheDocument()
  expect(screen.getByText('深色')).toBeInTheDocument()
  expect(screen.getByText('跟隨系統')).toBeInTheDocument()
})
```

- [ ] **Step 2: Run tests and confirm RED**

Run: `cd frontend && pnpm test -- ThemeSwitcher PortalLayout`  
Expected: FAIL because `ThemeSwitcher` does not exist.

- [ ] **Step 3: Implement the isolated switcher and system default**

```tsx
const themes = [
  { value: 'light', label: '淺色', icon: Sun },
  { value: 'dark', label: '深色', icon: Moon },
  { value: 'system', label: '跟隨系統', icon: Monitor },
] as const

export default function ThemeSwitcher() {
  const { theme, setTheme } = useTheme()
  return (
    <div role="group" aria-label="切換顯示主題">
      {themes.map(({ value, label, icon: Icon }) => (
        <Button
          key={value}
          type="button"
          variant={theme === value ? 'secondary' : 'ghost'}
          aria-pressed={theme === value}
          onClick={() => setTheme(value)}
        >
          <Icon aria-hidden="true" />
          {label}
        </Button>
      ))}
    </div>
  )
}
```

Change `Providers` to `defaultTheme="system"`, retain `enableSystem`, and mount the switcher in both desktop and mobile user sections.

- [ ] **Step 4: Run focused and full frontend verification**

Run: `cd frontend && pnpm test && pnpm lint`  
Expected: all tests and ESLint pass with no warning.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/theme frontend/src/components/layout
git commit -m "feat: add accessible theme switching"
```

### Task 2: Recurrence domain contract

**Files:**
- Create: `backend/FamilyPortal.Domain/Enums/LeapMonthPolicy.cs`
- Modify: `backend/FamilyPortal.Domain/ValueObjects/RecurrenceRule.cs`
- Modify: `backend/FamilyPortal.Application/DTOs/Common/ValueObjectRequests.cs`
- Modify: `backend/FamilyPortal.Application/DTOs/Event/EventDtos.cs`
- Test: `backend/FamilyPortal.Security.Tests/ValidationBoundaryTests.cs`

**Interfaces:**
- Produces: `LeapMonthPolicy { RegularOnly, LeapOnly, Both }`.
- Produces recurrence properties: `LeapMonthPolicy`, `EffectiveFrom`, `EffectiveUntil`.

- [ ] **Step 1: Add failing validation cases**

```csharp
[Fact]
public void RecurrenceRuleRequest_RejectsEndBeforeStart()
{
    var request = new RecurrenceRuleRequest {
        Type = RecurrenceType.LunarYearly,
        EffectiveFrom = new DateTime(2036, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        EffectiveUntil = new DateTime(2036, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };
    Assert.False(IsValid(request));
}
```

- [ ] **Step 2: Run test and confirm RED**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter RecurrenceRuleRequest`  
Expected: compilation fails because the new properties do not exist.

- [ ] **Step 3: Add domain enum, fields, DTO mapping and validation**

```csharp
public enum LeapMonthPolicy { RegularOnly = 0, LeapOnly = 1, Both = 2 }

public class RecurrenceRule {
    public RecurrenceType Type { get; set; }
    public int Interval { get; set; } = 1;
    public List<int>? LunarDays { get; set; }
    public LeapMonthPolicy LeapMonthPolicy { get; set; } = LeapMonthPolicy.RegularOnly;
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveUntil { get; set; }
}
```

Validate enum membership, UTC-normalized effective dates, paired lunar fields, and end not before start. Copy every field in `EventService.MapRecurrenceRule`.

- [ ] **Step 4: Run backend tests**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj`  
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/FamilyPortal.Domain backend/FamilyPortal.Application backend/FamilyPortal.Security.Tests
git commit -m "feat: model bounded lunar recurrence rules"
```

### Task 3: Lunar recurrence service

**Files:**
- Create: `backend/FamilyPortal.Application/Models/LunarOccurrence.cs`
- Create: `backend/FamilyPortal.Application/Interfaces/ILunarRecurrenceService.cs`
- Create: `backend/FamilyPortal.Infrastructure/Calendar/LunarRecurrenceService.cs`
- Modify: `backend/FamilyPortal.Infrastructure/DependencyInjection.cs`
- Test: `backend/FamilyPortal.Security.Tests/Application/LunarRecurrenceServiceTests.cs`

**Interfaces:**
- Consumes: `Event rule`, UTC `rangeStart`, UTC `rangeEnd`.
- Produces: `Task<IReadOnlyList<LunarOccurrence>> ExpandAsync(Event rule, DateTime rangeStart, DateTime rangeEnd, CancellationToken)`.

- [ ] **Step 1: Write failing 2026/2036, leap-month and short-month tests**

```csharp
[Theory]
[InlineData(2026)]
[InlineData(2036)]
public async Task ExpandAsync_ProducesOccurrenceForRequestedYear(int year)
{
    var result = await service.ExpandAsync(Rule(8, 15),
        new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        new DateTime(year, 12, 31, 23, 59, 59, DateTimeKind.Utc), default);
    Assert.Single(result);
    Assert.Equal(8, result[0].LunarMonth);
    Assert.Equal(15, result[0].LunarDay);
}
```

- [ ] **Step 2: Run test and confirm RED**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter LunarRecurrenceServiceTests`  
Expected: FAIL because the service is missing.

- [ ] **Step 3: Implement range-bounded expansion**

Use .NET `System.Globalization.ChineseLunisolarCalendar` only inside Infrastructure, including `GetLeapMonth(year)` to map regular versus leap months. Iterate the lunar years intersecting the requested solar range, apply `LeapMonthPolicy`, catch only `ArgumentOutOfRangeException` for invalid calendar dates, and emit stable IDs:

```csharp
var occurrenceId = $"{rule.Id}:{solarDate:yyyy-MM-dd}";
```

Register `ILunarRecurrenceService` in `DependencyInjection` and keep conversion types out of Application signatures.

- [ ] **Step 4: Verify exact known dates and all backend tests**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj`  
Expected: known date, leap policy, missing lunar day and range-boundary tests pass.

- [ ] **Step 5: Commit**

```bash
git add backend/FamilyPortal.Application backend/FamilyPortal.Infrastructure backend/FamilyPortal.Security.Tests
git commit -m "feat: expand lunar recurrence by date range"
```

### Task 4: Event query integration and UI

**Files:**
- Modify: `backend/FamilyPortal.Application/Interfaces/IEventRepository.cs`
- Modify: `backend/FamilyPortal.Infrastructure/MongoDB/EventRepository.cs`
- Modify: `backend/FamilyPortal.Application/Services/EventService.cs`
- Modify: `frontend/src/types/event.ts`
- Modify: `frontend/src/components/features/calendar/EventDialog.tsx`
- Test: `backend/FamilyPortal.Security.Tests/Application/EventServiceTests.cs`
- Test: `frontend/src/app/(portal)/calendar/__tests__/CalendarPage.test.tsx`

**Interfaces:**
- Produces: repository query `GetRecurringRulesAsync(DateTime start, DateTime end, int limit, CancellationToken)`.
- Produces: `EventResponse.IsOccurrence` and `EventResponse.SourceRuleId`.

- [ ] **Step 1: Write failing merge and form payload tests**

Assert a range query returns one stored event plus one generated lunar occurrence, ordered by `StartDate`; assert the form submits `LunarYearly` or `LunarMonthly`, month/day, leap policy and effective dates.

- [ ] **Step 2: Run focused tests and confirm RED**

Run: `dotnet test backend/FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter EventServiceTests`  
Run: `cd frontend && pnpm test -- CalendarPage`  
Expected: FAIL because occurrences and form controls are absent.

- [ ] **Step 3: Merge occurrences and implement recurrence controls**

`EventService.GetByDateRangeAsync` fetches stored events and active rules, calls `ExpandAsync`, maps occurrences without persistence, de-duplicates by stable ID, orders and caps at `MaxPageSize`. The form reveals recurrence controls only when `isLunarDate` is true and preserves existing values when editing.

- [ ] **Step 4: Run regression builds**

Run: `dotnet test backend/FamilyPortal.slnx`  
Run: `cd frontend && pnpm test && pnpm lint && ./node_modules/.bin/next build && pnpm build:pages && pnpm test:pages`  
Expected: every command exits 0; Pages remains static and fictional.

- [ ] **Step 5: Commit**

```bash
git add backend frontend
git commit -m "feat: display recurring lunar calendar events"
```
