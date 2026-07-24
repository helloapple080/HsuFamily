# Backend Architecture and Security Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 將業務流程移入 Application Services，補齊所有輸入與查詢上限，修正 token history、rate-limit、MongoDB 索引與初始化競態，同時維持 API 相容。

**Architecture:** Controller 只處理 HTTP、Cookie、CSRF 與 `ApiResponse<T>`；Application Services 協調 repositories、token/password abstractions、mapping 與通知；Infrastructure 實作 MongoDB、JWT、Argon2id 與 SignalR publisher。Domain 不新增 Application／Infrastructure 依賴。

**Tech Stack:** C# 14、.NET 10、ASP.NET Core Controllers、MongoDB Driver 3.7、SignalR、xUnit、`Microsoft.AspNetCore.Mvc.Testing`。

## Global Constraints

- 不執行資料庫 drop、collection 清空、volume 刪除或破壞性 migration。
- 現有 API route、HTTP success status、`ApiResponse<T>` envelope 與 JSON 核心欄位保持相容。
- Domain 僅保留既有 MongoDB.Bson 標注依賴；Application 只 project-reference Domain。
- 所有 public 方法補繁體中文 XML 文件註解；本次新增或修改的非同步路徑接受並向下傳遞 `CancellationToken`。
- 分頁與搜尋上限 100；事件日期區間最大 62 天；request body 最大 1 MiB。
- 農曆月 1–12、日 1–30；recurrence interval 1–120。
- Refresh Token 保留所有有效 session，歷史最多 50 筆，資料庫只保存 hash。
- 所有行為修正先寫會失敗的測試，再寫最小 production code。

---

## File Map

- `FamilyPortal.Application/Common/ValidationLimits.cs`：單一資源限制來源。
- `FamilyPortal.Application/Common/ServiceResult.cs`：Application 到 Controller 的狀態契約。
- `FamilyPortal.Application/DTOs/*`：transport-safe request／response、巢狀輸入驗證。
- `FamilyPortal.Application/Interfaces/I*Service.cs`：Controller 使用的 use-case ports。
- `FamilyPortal.Application/Services/*Service.cs`：業務流程、mapping、正規化。
- `FamilyPortal.Application/Interfaces/ICalendarNotifier.cs`：即時通知 port。
- `FamilyPortal.Infrastructure/Hubs/SignalRCalendarNotifier.cs`：SignalR adapter。
- `FamilyPortal.Infrastructure/MongoDB/*Repository.cs`：bounded queries 與 atomic updates。
- `FamilyPortal.Api/Controllers/*Controller.cs`：薄 HTTP adapters。
- `FamilyPortal.Api/Program.cs`：DI、middleware ordering、body limit、bootstrap。
- `FamilyPortal.Security.Tests/Application/*Tests.cs`：service unit tests。
- `FamilyPortal.Security.Tests/ArchitectureDependencyTests.cs`：禁止依賴測試。
- `FamilyPortal.Security.Tests/ValidationBoundaryTests.cs`：DTO 邊界測試。

### Task 1: Establish Application Result and Architecture Guards

**Files:**
- Create: `backend/FamilyPortal.Application/Common/ServiceError.cs`
- Create: `backend/FamilyPortal.Application/Common/ServiceResult.cs`
- Create: `backend/FamilyPortal.Security.Tests/ArchitectureDependencyTests.cs`

**Interfaces:**
- Produces: `ServiceResult<T>.Success(T)`、`ServiceResult<T>.Failure(ServiceError, string)`、`IsSuccess`、`Value`、`Error`、`Message`。

- [ ] **Step 1: Write the failing architecture tests**

```csharp
public class ArchitectureDependencyTests
{
    [Fact]
    public void Controllers_MustNotReferenceInfrastructureNamespaces()
    {
        var controllerFiles = Directory.GetFiles(ApiSourcePath(), "*Controller.cs");
        foreach (var file in controllerFiles)
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain("using FamilyPortal.Infrastructure", source);
        }
    }

    [Fact]
    public void ApplicationProject_MustOnlyReferenceDomainProject()
    {
        var project = File.ReadAllText(ApplicationProjectPath());
        Assert.Contains("FamilyPortal.Domain.csproj", project);
        Assert.DoesNotContain("FamilyPortal.Infrastructure.csproj", project);
        Assert.DoesNotContain("FamilyPortal.Api.csproj", project);
    }
}
```

Resolve paths from `AppContext.BaseDirectory` by walking upward until `backend/FamilyPortal.slnx` exists. The first test must fail on current Auth／Events controllers.

- [ ] **Step 2: Run RED**

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter ArchitectureDependencyTests
```

Expected: Controller source assertion fails.

- [ ] **Step 3: Add the shared result contract**

```csharp
namespace FamilyPortal.Application.Common;

/// <summary>Application use case 可回傳的失敗類型。</summary>
public enum ServiceError { None, Validation, Unauthorized, Forbidden, NotFound, Conflict }

/// <summary>不依賴 HTTP 的 Application 執行結果。</summary>
public sealed record ServiceResult<T>(bool IsSuccess, T? Value, ServiceError Error, string? Message)
{
    /// <summary>建立成功結果。</summary>
    public static ServiceResult<T> Success(T value) => new(true, value, ServiceError.None, null);

    /// <summary>建立失敗結果。</summary>
    public static ServiceResult<T> Failure(ServiceError error, string message) => new(false, default, error, message);
}
```

Do not weaken the failing controller assertion yet; later service tasks make it green.

- [ ] **Step 4: Commit the guard and contract**

```bash
git add backend/FamilyPortal.Application/Common backend/FamilyPortal.Security.Tests/ArchitectureDependencyTests.cs
git commit -m "test: add clean architecture dependency guard"
```

### Task 2: Add Central Validation Limits and Transport DTOs

**Files:**
- Create: `backend/FamilyPortal.Application/Common/ValidationLimits.cs`
- Create: `backend/FamilyPortal.Application/DTOs/Common/ValueObjectRequests.cs`
- Modify: `backend/FamilyPortal.Application/DTOs/Auth/AuthDtos.cs`
- Modify: `backend/FamilyPortal.Application/DTOs/Event/EventDtos.cs`
- Modify: `backend/FamilyPortal.Application/DTOs/Member/MemberDtos.cs`
- Modify: `backend/FamilyPortal.Application/DTOs/Itinerary/ItineraryDtos.cs`
- Create: `backend/FamilyPortal.Security.Tests/ValidationBoundaryTests.cs`

**Interfaces:**
- Produces: `ValidationLimits` constants and validated `LunarDateRequest`, `RecurrenceRuleRequest`, `WorshipDetailsRequest`, `ContactInfoRequest`, `ParticipantRequest`.

- [ ] **Step 1: Write boundary tests**

Use `Validator.TryValidateObject(..., validateAllProperties: true)` and assert:

```csharp
[Theory]
[InlineData(0, 1)]
[InlineData(13, 1)]
[InlineData(1, 0)]
[InlineData(1, 31)]
public void LunarDateRequest_RejectsOutOfRangeValues(int month, int day)
{
    var request = new LunarDateRequest { Year = 2026, Month = month, Day = day };
    Assert.False(IsValid(request));
}

[Fact]
public void UpsertEventRequest_RejectsOversizedCollections()
{
    var request = ValidEvent();
    request.Tags = Enumerable.Repeat("family", ValidationLimits.MaxTags + 1).ToList();
    Assert.False(IsValid(request));
}

[Fact]
public void EventRangeRequest_RejectsMoreThanSixtyTwoDays()
{
    var request = new EventRangeRequest
    {
        Start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        End = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc)
    };
    Assert.False(IsValid(request));
}
```

- [ ] **Step 2: Run RED**

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter ValidationBoundaryTests
```

Expected: compile failure because validation types do not exist.

- [ ] **Step 3: Create exact limits**

```csharp
namespace FamilyPortal.Application.Common;

/// <summary>API 與資料存取共用的資源上限。</summary>
public static class ValidationLimits
{
    public const int MaxPageSize = 100;
    public const int MaxDateRangeDays = 62;
    public const int MaxTitleLength = 200;
    public const int MaxDescriptionLength = 4000;
    public const int MaxShortTextLength = 200;
    public const int MaxNotesLength = 8000;
    public const int MaxUrlLength = 2048;
    public const int MaxTags = 20;
    public const int MaxTagLength = 50;
    public const int MaxRelatedMembers = 100;
    public const int MaxParticipants = 100;
    public const int MaxPhotos = 100;
    public const int MaxRecurrenceInterval = 120;
}
```

Create request DTOs with `[Range]`, `[MaxLength]` and `IValidatableObject`. Example:

```csharp
public sealed class LunarDateRequest
{
    [Range(1900, 2200)] public int Year { get; set; }
    [Range(1, 12)] public int Month { get; set; }
    [Range(1, 30)] public int Day { get; set; }
    public bool IsLeapMonth { get; set; }
    [MaxLength(32)] public string? DisplayText { get; set; }
}

public sealed class EventRangeRequest : IValidatableObject
{
    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (End < Start) yield return new("結束日期不可早於開始日期", [nameof(End)]);
        if (End - Start > TimeSpan.FromDays(ValidationLimits.MaxDateRangeDays))
            yield return new($"日期範圍不可超過 {ValidationLimits.MaxDateRangeDays} 天", [nameof(End)]);
    }
}
```

Replace Domain value objects in request DTO properties with request DTOs, while response DTOs keep existing JSON shapes. Add collection validation inside each `Validate` method because DataAnnotations does not limit list counts or item lengths.

- [ ] **Step 4: Validate all defined boundaries**

Run:

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter ValidationBoundaryTests
```

Expected: tests pass for min/max accepted values and one-beyond rejected values.

- [ ] **Step 5: Commit**

```bash
git add backend/FamilyPortal.Application/Common/ValidationLimits.cs backend/FamilyPortal.Application/DTOs backend/FamilyPortal.Security.Tests/ValidationBoundaryTests.cs
git commit -m "fix: enforce api resource boundaries"
```

### Task 3: Introduce Calendar Notification Port and Event Service

**Files:**
- Create: `backend/FamilyPortal.Application/Interfaces/ICalendarNotifier.cs`
- Create: `backend/FamilyPortal.Application/Interfaces/IEventService.cs`
- Create: `backend/FamilyPortal.Application/Services/EventService.cs`
- Create: `backend/FamilyPortal.Infrastructure/Hubs/SignalRCalendarNotifier.cs`
- Modify: `backend/FamilyPortal.Infrastructure/DependencyInjection.cs`
- Modify: `backend/FamilyPortal.Api/Controllers/EventsController.cs`
- Create: `backend/FamilyPortal.Security.Tests/Application/EventServiceTests.cs`

**Interfaces:**
- Produces: `IEventService.GetByDateRangeAsync`／`GetByIdAsync`／`CreateAsync`／`UpdateAsync`／`DeleteAsync`; `ICalendarNotifier.EventChangedAsync`.

- [ ] **Step 1: Write failing EventService unit tests**

With in-memory fake repository and notifier, assert:

```csharp
[Fact]
public async Task CreateAsync_NormalizesInputAndPublishesCreatedEvent()
{
    var result = await service.CreateAsync(ValidRequest(), "user-1", CancellationToken.None);
    Assert.True(result.IsSuccess);
    Assert.Equal("user-1", repository.Created!.CreatedBy);
    Assert.Equal(DateTimeKind.Utc, repository.Created.StartDate.Kind);
    Assert.Equal(("EventCreated", result.Value!.Id), notifier.LastEvent);
}

[Fact]
public async Task UpdateAsync_MissingEventReturnsNotFoundWithoutNotification()
{
    var result = await service.UpdateAsync("missing", ValidRequest(), "user-1", CancellationToken.None);
    Assert.Equal(ServiceError.NotFound, result.Error);
    Assert.Null(notifier.LastEvent);
}
```

- [ ] **Step 2: Run RED**

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter EventServiceTests
```

Expected: compile failure because service interfaces are absent.

- [ ] **Step 3: Define ports and implement service**

```csharp
public interface ICalendarNotifier
{
    /// <summary>通知所有已驗證連線行事曆資料已變更。</summary>
    Task EventChangedAsync(string eventName, EventResponse response, CancellationToken cancellationToken);
}

public interface IEventService
{
    Task<ServiceResult<IReadOnlyList<EventResponse>>> GetByDateRangeAsync(EventRangeRequest request, CancellationToken cancellationToken);
    Task<ServiceResult<EventResponse>> GetByIdAsync(string id, CancellationToken cancellationToken);
    Task<ServiceResult<EventResponse>> CreateAsync(UpsertEventRequest request, string userId, CancellationToken cancellationToken);
    Task<ServiceResult<EventResponse>> UpdateAsync(string id, UpsertEventRequest request, string userId, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> DeleteAsync(string id, CancellationToken cancellationToken);
}
```

`EventService` maps requests to Domain objects, normalizes all dates with `.ToUniversalTime()`, generates canonical lunar display text, updates `UpdatedAt = DateTime.UtcNow`, and calls notifier only after repository success.

Infrastructure adapter:

```csharp
public sealed class SignalRCalendarNotifier(IHubContext<CalendarHub> hub) : ICalendarNotifier
{
    /// <inheritdoc />
    public Task EventChangedAsync(string eventName, EventResponse response, CancellationToken cancellationToken) =>
        hub.Clients.All.SendAsync(eventName, response, cancellationToken);
}
```

- [ ] **Step 4: Thin the controller and register DI**

Controller constructor accepts only `IEventService`. Example action:

```csharp
[HttpPost]
[Authorize(Policy = "AdminOnly")]
public async Task<ActionResult<ApiResponse<EventResponse>>> Create(
    [FromBody] UpsertEventRequest request,
    CancellationToken cancellationToken)
{
    var result = await _service.CreateAsync(request, User.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);
    return result.IsSuccess
        ? CreatedAtAction(nameof(GetById), new { id = result.Value!.Id }, ApiResponse<EventResponse>.Ok(result.Value, "事件建立成功"))
        : ToActionResult(result);
}
```

Register `IEventService` in API DI and `ICalendarNotifier` in Infrastructure DI.

- [ ] **Step 5: Run tests and commit**

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter EventServiceTests
dotnet build FamilyPortal.slnx --configuration Release
```

Expected: tests and build pass.

```bash
git add backend/FamilyPortal.Application backend/FamilyPortal.Infrastructure/Hubs backend/FamilyPortal.Infrastructure/DependencyInjection.cs backend/FamilyPortal.Api/Controllers/EventsController.cs backend/FamilyPortal.Api/Program.cs backend/FamilyPortal.Security.Tests/Application/EventServiceTests.cs
git commit -m "refactor: move event workflows to application service"
```

### Task 4: Add Member Service and Canonical Lunar Birthday Mapping

**Files:**
- Create: `backend/FamilyPortal.Application/Interfaces/IMemberService.cs`
- Create: `backend/FamilyPortal.Application/Services/MemberService.cs`
- Modify: `backend/FamilyPortal.Application/Interfaces/IMemberRepository.cs`
- Modify: `backend/FamilyPortal.Infrastructure/MongoDB/MemberRepository.cs`
- Modify: `backend/FamilyPortal.Api/Controllers/MembersController.cs`
- Create: `backend/FamilyPortal.Security.Tests/Application/MemberServiceTests.cs`

**Interfaces:**
- Produces: bounded member list and server-generated `LunarDate.DisplayText`.

- [ ] **Step 1: Write failing member tests**

```csharp
[Fact]
public async Task CreateAsync_RebuildsUntrustedLunarDisplayText()
{
    var request = ValidRequest();
    request.LunarBirthday = new LunarDateRequest
    {
        Year = 2026, Month = 1, Day = 1, DisplayText = "不可信內容"
    };
    var result = await service.CreateAsync(request, CancellationToken.None);
    Assert.Equal("農曆正月初一", result.Value!.LunarBirthday!.DisplayText);
}

[Fact]
public async Task GetActiveAsync_RequestsAtMostOneHundredMembers()
{
    await service.GetActiveAsync(CancellationToken.None);
    Assert.Equal(ValidationLimits.MaxPageSize, repository.LastLimit);
}
```

- [ ] **Step 2: Run RED, implement and verify**

Change repository signature to:

```csharp
Task<List<Member>> GetActiveOrderedAsync(int limit, CancellationToken cancellationToken = default);
```

Implementation applies `.Limit(Math.Clamp(limit, 1, ValidationLimits.MaxPageSize))`. `MemberService` creates display text through a pure formatter:

```csharp
private static string FormatLunarDate(int month, int day, bool isLeapMonth) =>
    $"農曆{(isLeapMonth ? "閏" : string.Empty)}{LunarMonths[month - 1]}月{LunarDays[day - 1]}";
```

Run:

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter MemberServiceTests
```

Expected: tests pass, including 正月／初一 and leap prefix.

- [ ] **Step 3: Replace controller repository use and commit**

Controller accepts only `IMemberService`, maps `NotFound` to 404, and preserves existing routes and envelopes.

```bash
git add backend/FamilyPortal.Application backend/FamilyPortal.Infrastructure/MongoDB/MemberRepository.cs backend/FamilyPortal.Api/Controllers/MembersController.cs backend/FamilyPortal.Api/Program.cs backend/FamilyPortal.Security.Tests/Application/MemberServiceTests.cs
git commit -m "refactor: move member workflows to application service"
```

### Task 5: Bound Itinerary Queries and Add Itinerary Service

**Files:**
- Create: `backend/FamilyPortal.Application/Interfaces/IItineraryService.cs`
- Create: `backend/FamilyPortal.Application/Services/ItineraryService.cs`
- Modify: `backend/FamilyPortal.Application/Interfaces/IItineraryRepository.cs`
- Modify: `backend/FamilyPortal.Infrastructure/MongoDB/ItineraryRepository.cs`
- Modify: `backend/FamilyPortal.Infrastructure/MongoDB/MongoDbIndexInitializer.cs`
- Modify: `backend/FamilyPortal.Api/Controllers/ItinerariesController.cs`
- Create: `backend/FamilyPortal.Security.Tests/Application/ItineraryServiceTests.cs`

**Interfaces:**
- Produces: all itinerary reads capped at 100 and stable `(Date desc, Id desc)` ordering.

- [ ] **Step 1: Write failing query-bound tests**

```csharp
[Theory]
[InlineData(0, 1)]
[InlineData(101, 100)]
[InlineData(50, 50)]
public async Task SearchAsync_ClampsLimit(int requested, int expected)
{
    await service.SearchAsync("京都", requested, CancellationToken.None);
    Assert.Equal(expected, repository.LastLimit);
}

[Fact]
public async Task ParticipantQuery_UsesBoundedStablePage()
{
    await service.GetByParticipantAsync("member-1", 100, CancellationToken.None);
    Assert.Equal(100, repository.LastLimit);
}
```

- [ ] **Step 2: Run RED and implement bounded repository signatures**

```csharp
Task<List<Itinerary>> GetByParticipantAsync(string memberId, int limit, CancellationToken cancellationToken = default);
Task<List<Itinerary>> SearchAsync(string keyword, int limit, CancellationToken cancellationToken = default);
```

Both methods call `Math.Clamp(limit, 1, ValidationLimits.MaxPageSize)`, sort by `Date` then `Id`, and pass `cancellationToken` to `ToListAsync`.

- [ ] **Step 3: Correct the ESR index without dropping existing indexes**

Add this idempotent named index:

```csharp
new(
    Builders<Itinerary>.IndexKeys
        .Ascending(i => i.Tags)
        .Descending(i => i.Date)
        .Descending(i => i.Id),
    new CreateIndexOptions { Name = "idx_tags_date_id" })
```

Do not remove old indexes automatically; document later that redundant index cleanup requires staging query evidence.

- [ ] **Step 4: Thin controller, run tests and commit**

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter ItineraryServiceTests
dotnet build FamilyPortal.slnx --configuration Release
git add backend/FamilyPortal.Application backend/FamilyPortal.Infrastructure/MongoDB/ItineraryRepository.cs backend/FamilyPortal.Infrastructure/MongoDB/MongoDbIndexInitializer.cs backend/FamilyPortal.Api/Controllers/ItinerariesController.cs backend/FamilyPortal.Api/Program.cs backend/FamilyPortal.Security.Tests/Application/ItineraryServiceTests.cs
git commit -m "refactor: bound itinerary application workflows"
```

Expected: tests and build pass; search and participant endpoints preserve their JSON array responses.

### Task 6: Extract Authentication Service and Bound Token History

**Files:**
- Create: `backend/FamilyPortal.Application/Interfaces/IAuthService.cs`
- Create: `backend/FamilyPortal.Application/Services/AuthService.cs`
- Modify: `backend/FamilyPortal.Application/Interfaces/ITokenService.cs`
- Modify: `backend/FamilyPortal.Application/Interfaces/IUserRepository.cs`
- Modify: `backend/FamilyPortal.Infrastructure/Security/JwtTokenService.cs`
- Modify: `backend/FamilyPortal.Infrastructure/MongoDB/UserRepository.cs`
- Modify: `backend/FamilyPortal.Api/Controllers/AuthController.cs`
- Create: `backend/FamilyPortal.Security.Tests/Application/AuthServiceTests.cs`
- Create: `backend/FamilyPortal.Security.Tests/RefreshTokenHistoryTests.cs`

**Interfaces:**
- Produces: `AuthSession` containing cookie-only raw tokens, expiry and public `UserInfo`; `IAuthService` owns login／refresh／logout／register flows.

- [ ] **Step 1: Write failing authentication workflow tests**

```csharp
[Fact]
public async Task LoginAsync_InvalidPasswordReturnsGenericUnauthorized()
{
    passwordHasher.VerifyResult = false;
    var result = await service.LoginAsync("member", "wrong", CancellationToken.None);
    Assert.Equal(ServiceError.Unauthorized, result.Error);
    Assert.Equal("帳號或密碼錯誤", result.Message);
}

[Fact]
public async Task RefreshAsync_ReplayedTokenRevokesFamily()
{
    repository.User = UserWithRevokedReplacedToken();
    var result = await service.RefreshAsync("raw-token", CancellationToken.None);
    Assert.Equal(ServiceError.Unauthorized, result.Error);
    Assert.Equal(repository.User.RefreshTokens[0].FamilyId, repository.RevokedFamilyId);
}
```

- [ ] **Step 2: Define auth contract and move business logic**

```csharp
public sealed record AuthSession(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    UserInfo User);

public interface IAuthService
{
    Task<ServiceResult<AuthSession>> LoginAsync(string identifier, string password, CancellationToken cancellationToken);
    Task<ServiceResult<AuthSession>> RefreshAsync(string rawRefreshToken, CancellationToken cancellationToken);
    Task<ServiceResult<bool>> LogoutAsync(string? userId, CancellationToken cancellationToken);
    Task<ServiceResult<UserInfo>> GetUserAsync(string userId, CancellationToken cancellationToken);
    Task<ServiceResult<UserInfo>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
}
```

Change token generation to include expiry determined by the Infrastructure settings:

```csharp
public sealed record GeneratedAccessToken(string Token, DateTime ExpiresAt);
GeneratedAccessToken GenerateAccessToken(User user);
```

Controller keeps only antiforgery token creation, Cookie append/delete, request binding, log-safe IP messages and `ServiceResult` to HTTP mapping. It no longer imports `FamilyPortal.Infrastructure.Security` or `IOptions<JwtSettings>`.

- [ ] **Step 3: Add an atomic 50-entry token-history bound**

Use MongoDB push with negative slice:

```csharp
private const int MaxRefreshTokenHistory = 50;

var update = Builders<User>.Update
    .Set("refreshTokens.$.revokedAt", now)
    .Set("refreshTokens.$.replacedByTokenHash", newRefreshToken.TokenHash)
    .PushEach(u => u.RefreshTokens, [newRefreshToken], slice: -MaxRefreshTokenHistory)
    .Set(u => u.UpdatedAt, now);
```

Add a test that renders the update definition and asserts `$slice` equals `-50`; retain the existing atomic filter for active current hash.

- [ ] **Step 4: Run auth tests and architecture guard**

```bash
cd backend
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --filter 'AuthServiceTests|RefreshTokenHistoryTests|ArchitectureDependencyTests'
```

Expected: auth workflows pass and AuthController contains no Infrastructure namespace.

- [ ] **Step 5: Commit**

```bash
git add backend/FamilyPortal.Application backend/FamilyPortal.Infrastructure/Security/JwtTokenService.cs backend/FamilyPortal.Infrastructure/MongoDB/UserRepository.cs backend/FamilyPortal.Api/Controllers/AuthController.cs backend/FamilyPortal.Api/Program.cs backend/FamilyPortal.Security.Tests/Application/AuthServiceTests.cs backend/FamilyPortal.Security.Tests/RefreshTokenHistoryTests.cs
git commit -m "refactor: move authentication to application service"
```

### Task 7: Fix Middleware Ordering, Body Limits and Admin Bootstrap Race

**Files:**
- Modify: `backend/FamilyPortal.Api/Program.cs`
- Modify: `backend/FamilyPortal.Application/Interfaces/IUserRepository.cs`
- Modify: `backend/FamilyPortal.Infrastructure/MongoDB/UserRepository.cs`
- Modify: `backend/FamilyPortal.Security.Tests/ApiSecurityIntegrationTests.cs`
- Create: `backend/FamilyPortal.Security.Tests/AdminBootstrapTests.cs`

**Interfaces:**
- Produces: authenticated-user limiter partition, 1 MiB body ceiling and duplicate-safe default admin bootstrap.

- [ ] **Step 1: Add failing middleware and bootstrap tests**

Add an integration endpoint probe which authenticates two users behind the same test IP and asserts independent `RateLimit-*` consumption. Add repository test:

```csharp
[Fact]
public async Task EnsureDefaultAdminAsync_DuplicateInsertIsTreatedAsExisting()
{
    fakeCollection.InsertException = DuplicateKeyException();
    var created = await repository.TryCreateAsync(DefaultAdmin(), CancellationToken.None);
    Assert.False(created);
}
```

- [ ] **Step 2: Reorder and harden the pipeline**

The relevant order must be:

```csharp
app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseCors("FrontendPolicy");
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
```

Partition the general limiter with immutable name identifier:

```csharp
partitionKey: context.User.FindFirstValue(ClaimTypes.NameIdentifier)
    ?? context.Connection.RemoteIpAddress?.ToString()
    ?? "unknown"
```

Set Kestrel request body limit:

```csharp
builder.WebHost.ConfigureKestrel(options =>
    options.Limits.MaxRequestBodySize = 1024 * 1024);
```

- [ ] **Step 3: Make bootstrap insert duplicate-safe**

Add:

```csharp
Task<bool> TryCreateAsync(User user, CancellationToken cancellationToken = default);
```

Infrastructure catches only Mongo duplicate-key category and returns false; all other failures propagate. Program logs user ID only when true and logs an informational existing-admin message when false.

- [ ] **Step 4: Run the complete backend verification**

```bash
cd backend
dotnet build FamilyPortal.slnx --configuration Release
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --configuration Release --collect:"XPlat Code Coverage"
```

Expected: build and all tests pass; architecture test confirms every Controller is Infrastructure-free.

- [ ] **Step 5: Commit**

```bash
git add backend/FamilyPortal.Api/Program.cs backend/FamilyPortal.Application/Interfaces/IUserRepository.cs backend/FamilyPortal.Infrastructure/MongoDB/UserRepository.cs backend/FamilyPortal.Security.Tests
git commit -m "fix: harden api resource and startup controls"
```

## Plan Acceptance

Run:

```bash
rg -n 'using FamilyPortal\.Infrastructure' backend/FamilyPortal.Api/Controllers
rg -n '\.Result|\.Wait\(' backend --glob '*.cs'
cd backend
dotnet build FamilyPortal.slnx --configuration Release
dotnet test FamilyPortal.Security.Tests/FamilyPortal.Security.Tests.csproj --configuration Release
```

Expected: both `rg` commands return no matches; build and tests pass. Existing MongoDB documents are not rewritten, and only future writes receive the stricter validation.
