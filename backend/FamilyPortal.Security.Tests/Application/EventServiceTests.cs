using System.Linq.Expressions;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Application.DTOs.Event;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using FamilyPortal.Application.Services;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Domain.ValueObjects;
using Xunit;

namespace FamilyPortal.Security.Tests.Application;

/// <summary>驗證行事曆事件 use case 的映射、錯誤與通知行為。</summary>
public sealed class EventServiceTests
{
    /// <summary>建立事件時應正規化日期、重建農曆顯示文字並於寫入後通知。</summary>
    [Fact]
    public async Task CreateAsync_NormalizesInputAndPublishesCreatedEvent()
    {
        var repository = new FakeEventRepository();
        var notifier = new FakeCalendarNotifier();
        var service = new EventService(repository, notifier, new FakeLunarRecurrenceService());
        var request = ValidRequest();
        request.StartDate = new DateTime(2026, 2, 17, 9, 0, 0, DateTimeKind.Local);
        request.EndDate = new DateTime(2026, 2, 17, 10, 0, 0, DateTimeKind.Local);
        request.IsLunarDate = true;
        request.LunarDate = new LunarDateRequest
        {
            Year = 2026,
            Month = 1,
            Day = 1,
            DisplayText = "不可信內容"
        };

        var result = await service.CreateAsync(request, "user-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Created);
        Assert.Equal("user-1", repository.Created.CreatedBy);
        Assert.Equal(DateTimeKind.Utc, repository.Created.StartDate.Kind);
        Assert.Equal(DateTimeKind.Utc, repository.Created.EndDate!.Value.Kind);
        Assert.Equal("農曆正月初一", repository.Created.LunarDate?.DisplayText);
        Assert.Equal("EventCreated", notifier.LastEvent?.Name);
        Assert.Equal(result.Value!.Id, notifier.LastEvent?.Id);
    }

    /// <summary>更新不存在的事件時應回傳 NotFound 且不通知。</summary>
    [Fact]
    public async Task UpdateAsync_MissingEventReturnsNotFoundWithoutNotification()
    {
        var repository = new FakeEventRepository();
        var notifier = new FakeCalendarNotifier();
        var service = new EventService(repository, notifier, new FakeLunarRecurrenceService());

        var result = await service.UpdateAsync(
            "missing",
            ValidRequest(),
            "user-1",
            CancellationToken.None);

        Assert.Equal(ServiceError.NotFound, result.Error);
        Assert.Null(notifier.LastEvent);
        Assert.Null(repository.Updated);
    }

    /// <summary>更新事件應保留建立中繼資料、正規化輸入，並於寫入後通知。</summary>
    [Fact]
    public async Task UpdateAsync_PreservesCreationMetadataAndPublishesUpdatedEvent()
    {
        var createdAt = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var repository = new FakeEventRepository
        {
            Stored = new Event
            {
                Id = "event-1",
                Title = "舊事件",
                CreatedBy = "creator-1",
                CreatedAt = createdAt,
                UpdatedAt = createdAt
            }
        };
        var notifier = new FakeCalendarNotifier();
        var service = new EventService(repository, notifier, new FakeLunarRecurrenceService());
        var request = ValidRequest();
        request.StartDate = DateTime.SpecifyKind(new DateTime(2026, 3, 1, 8, 0, 0), DateTimeKind.Unspecified);

        var result = await service.UpdateAsync(request: request, id: "event-1", userId: "editor-1", cancellationToken: CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(repository.Updated);
        Assert.Equal("event-1", repository.Updated.Id);
        Assert.Equal("creator-1", repository.Updated.CreatedBy);
        Assert.Equal(createdAt, repository.Updated.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, repository.Updated.StartDate.Kind);
        Assert.True(repository.Updated.UpdatedAt >= createdAt);
        Assert.Equal("EventUpdated", notifier.LastEvent?.Name);
        Assert.Equal("event-1", notifier.LastEvent?.Id);
    }

    /// <summary>無效事件類型應在存取資料庫前回傳 Validation。</summary>
    [Fact]
    public async Task CreateAsync_InvalidEventTypeReturnsValidationWithoutWriteOrNotification()
    {
        var repository = new FakeEventRepository();
        var notifier = new FakeCalendarNotifier();
        var service = new EventService(repository, notifier, new FakeLunarRecurrenceService());
        var request = ValidRequest();
        request.EventType = "not-an-event-type";

        var result = await service.CreateAsync(request, "user-1", CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Null(repository.Created);
        Assert.Null(notifier.LastEvent);
    }

    /// <summary>刪除事件應在資料庫成功後發布刪除通知。</summary>
    [Fact]
    public async Task DeleteAsync_ExistingEventDeletesThenPublishesNotification()
    {
        var repository = new FakeEventRepository
        {
            Stored = new Event { Id = "event-1", Title = "待刪除", CreatedBy = "creator-1" }
        };
        var notifier = new FakeCalendarNotifier();
        var service = new EventService(repository, notifier, new FakeLunarRecurrenceService());

        var result = await service.DeleteAsync("event-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("event-1", repository.DeletedId);
        Assert.Equal("EventDeleted", notifier.LastEvent?.Name);
        Assert.Equal("event-1", notifier.LastEvent?.Id);
    }

    /// <summary>查詢應將範圍正規化為 UTC 並將取消權杖傳給 Repository。</summary>
    [Fact]
    public async Task GetByDateRangeAsync_NormalizesRangeAndForwardsCancellationToken()
    {
        var repository = new FakeEventRepository();
        var service = new EventService(repository, new FakeCalendarNotifier(), new FakeLunarRecurrenceService());
        using var source = new CancellationTokenSource();
        var request = new EventRangeRequest
        {
            Start = DateTime.SpecifyKind(new DateTime(2026, 4, 1), DateTimeKind.Unspecified),
            End = DateTime.SpecifyKind(new DateTime(2026, 4, 30), DateTimeKind.Unspecified)
        };

        var result = await service.GetByDateRangeAsync(request, source.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(DateTimeKind.Utc, repository.LastRange!.Value.Start.Kind);
        Assert.Equal(DateTimeKind.Utc, repository.LastRange.Value.End.Kind);
        Assert.Equal(ValidationLimits.MaxPageSize, repository.LastRangeLimit);
        Assert.Equal(source.Token, repository.LastCancellationToken);
    }

    /// <summary>日期範圍查詢應合併已儲存事件與農曆 occurrence，並保留來源時間與期間。</summary>
    [Fact]
    public async Task GetByDateRangeAsync_MergesStoredEventsAndMappedLunarOccurrences()
    {
        var storedEvent = new Event
        {
            Id = "stored-1",
            Title = "已儲存事件",
            StartDate = new DateTime(2026, 9, 24, 0, 0, 0, DateTimeKind.Utc),
            IsAllDay = true,
            CreatedBy = "creator-1"
        };
        var recurringRule = new Event
        {
            Id = "rule-1",
            Title = "中秋聚會",
            StartDate = new DateTime(2025, 10, 6, 9, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 10, 6, 10, 30, 0, DateTimeKind.Utc),
            IsLunarDate = true,
            LunarDate = new LunarDate { Year = 2025, Month = 8, Day = 15 },
            RecurrenceRule = new RecurrenceRule { Type = RecurrenceType.LunarYearly },
            CreatedBy = "creator-1"
        };
        var repository = new FakeEventRepository
        {
            RangeEvents = [storedEvent],
            RecurringRules = [recurringRule]
        };
        var recurrenceService = new FakeLunarRecurrenceService
        {
            Occurrences =
            [
                new LunarOccurrence
                {
                    Id = "rule-1:2026-09-25",
                    SourceRuleId = "rule-1",
                    SolarDate = new DateTime(2026, 9, 25, 0, 0, 0, DateTimeKind.Utc),
                    LunarYear = 2026,
                    LunarMonth = 8,
                    LunarDay = 15
                }
            ]
        };
        var service = new EventService(repository, new FakeCalendarNotifier(), recurrenceService);
        using var source = new CancellationTokenSource();

        var result = await service.GetByDateRangeAsync(
            new EventRangeRequest
            {
                Start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc)
            },
            source.Token);

        Assert.True(result.IsSuccess);
        Assert.Collection(
            result.Value!,
            stored =>
            {
                Assert.Equal("stored-1", stored.Id);
                Assert.False(stored.IsOccurrence);
                Assert.Null(stored.SourceRuleId);
            },
            occurrence =>
            {
                Assert.Equal("rule-1:2026-09-25", occurrence.Id);
                Assert.True(occurrence.IsOccurrence);
                Assert.Equal("rule-1", occurrence.SourceRuleId);
                Assert.Equal(new DateTime(2026, 9, 25, 9, 0, 0, DateTimeKind.Utc), occurrence.StartDate);
                Assert.Equal(new DateTime(2026, 9, 25, 10, 30, 0, DateTimeKind.Utc), occurrence.EndDate);
                Assert.Equal("農曆八月十五", occurrence.LunarDate?.DisplayText);
            });
        Assert.Equal(source.Token, repository.LastRecurringCancellationToken);
    }

    /// <summary>同一日期已有穩定 occurrence 時，不應再輸出週期規則範本。</summary>
    [Fact]
    public async Task GetByDateRangeAsync_RemovesTemplateRepresentedByStableOccurrence()
    {
        var rule = new Event
        {
            Id = "rule-1",
            Title = "初一祭祖",
            StartDate = new DateTime(2026, 2, 17, 0, 0, 0, DateTimeKind.Utc),
            IsAllDay = true,
            IsLunarDate = true,
            LunarDate = new LunarDate { Year = 2026, Month = 1, Day = 1 },
            RecurrenceRule = new RecurrenceRule { Type = RecurrenceType.LunarYearly },
            CreatedBy = "creator-1"
        };
        var occurrence = new LunarOccurrence
        {
            Id = "rule-1:2026-02-17",
            SourceRuleId = "rule-1",
            SolarDate = rule.StartDate,
            LunarYear = 2026,
            LunarMonth = 1,
            LunarDay = 1
        };
        var repository = new FakeEventRepository { RangeEvents = [rule], RecurringRules = [rule] };
        var recurrenceService = new FakeLunarRecurrenceService { Occurrences = [occurrence, occurrence] };
        var service = new EventService(repository, new FakeCalendarNotifier(), recurrenceService);

        var result = await service.GetByDateRangeAsync(
            new EventRangeRequest
            {
                Start = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 2, 28, 23, 59, 59, DateTimeKind.Utc)
            },
            CancellationToken.None);

        var only = Assert.Single(result.Value!);
        Assert.Equal("rule-1:2026-02-17", only.Id);
        Assert.Equal(new DateTime(2026, 2, 17, 0, 0, 0, DateTimeKind.Utc), only.StartDate);
        Assert.Equal(DateTimeKind.Utc, only.StartDate.Kind);
    }

    /// <summary>週期規則不得在展開前截斷，否則排序後應保留的 occurrence 可能遺失。</summary>
    [Fact]
    public async Task GetByDateRangeAsync_CapsOnlyAfterExpandingAllRecurringRules()
    {
        var rules = Enumerable.Range(1, ValidationLimits.MaxPageSize + 1)
            .Select(index => new Event
            {
                Id = $"rule-{index:D3}",
                Title = $"規則 {index}",
                StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                IsAllDay = true,
                IsLunarDate = true,
                LunarDate = new LunarDate { Year = 2025, Month = 1, Day = 1 },
                RecurrenceRule = new RecurrenceRule { Type = RecurrenceType.LunarYearly },
                CreatedBy = "creator-1"
            })
            .ToList();
        var repository = new FakeEventRepository { RecurringRules = rules };
        var recurrenceService = new FakeLunarRecurrenceService
        {
            OccurrenceFactory = rule =>
            [
                OccurrenceFor(
                    rule.Id,
                    rule.Id == "rule-101"
                        ? new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)
                        : new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc))
            ]
        };
        var service = new EventService(repository, new FakeCalendarNotifier(), recurrenceService);

        var result = await service.GetByDateRangeAsync(
            new EventRangeRequest
            {
                Start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 9, 30, 23, 59, 59, DateTimeKind.Utc)
            },
            CancellationToken.None);

        Assert.Equal(ValidationLimits.MaxPageSize, result.Value!.Count);
        Assert.Equal("rule-101:2026-09-01", result.Value[0].Id);
        Assert.Contains(result.Value, response => response.SourceRuleId == "rule-101");
    }

    /// <summary>跨日 occurrence 的來源日期早於查詢起日時，只要期間仍重疊就必須輸出。</summary>
    [Fact]
    public async Task GetByDateRangeAsync_ExpandsBackByPositiveSourceDurationForOverlap()
    {
        var rule = new Event
        {
            Id = "overnight-rule",
            Title = "跨夜祭典",
            StartDate = new DateTime(2025, 1, 1, 23, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 1, 2, 2, 0, 0, DateTimeKind.Utc),
            IsLunarDate = true,
            LunarDate = new LunarDate { Year = 2025, Month = 1, Day = 1 },
            RecurrenceRule = new RecurrenceRule { Type = RecurrenceType.LunarYearly },
            CreatedBy = "creator-1"
        };
        var repository = new FakeEventRepository { RecurringRules = [rule] };
        var recurrenceService = new FakeLunarRecurrenceService
        {
            Occurrences =
            [
                OccurrenceFor(
                    rule.Id,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            ]
        };
        var service = new EventService(repository, new FakeCalendarNotifier(), recurrenceService);

        var result = await service.GetByDateRangeAsync(
            new EventRangeRequest
            {
                Start = new DateTime(2026, 1, 2, 0, 30, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc)
            },
            CancellationToken.None);

        var only = Assert.Single(result.Value!);
        Assert.Equal(new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc), only.StartDate);
        Assert.Equal(new DateTime(2026, 1, 2, 2, 0, 0, DateTimeKind.Utc), only.EndDate);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), recurrenceService.ExpansionRequests[0].Start);
    }

    /// <summary>規則結束日當晚開始的跨日 occurrence 仍須回傳，同批不重疊 occurrence 則須排除。</summary>
    [Fact]
    public async Task GetByDateRangeAsync_ReturnsFinalEffectiveUntilOccurrenceOnlyWhenItOverlaps()
    {
        var effectiveUntil = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var finalRule = new Event
        {
            Id = "final-overnight-rule",
            Title = "最後一晚祭典",
            StartDate = new DateTime(2025, 1, 1, 23, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 1, 2, 2, 0, 0, DateTimeKind.Utc),
            IsLunarDate = true,
            LunarDate = new LunarDate { Year = 2025, Month = 1, Day = 1 },
            RecurrenceRule = new RecurrenceRule
            {
                Type = RecurrenceType.LunarYearly,
                EffectiveUntil = effectiveUntil
            },
            CreatedBy = "creator-1"
        };
        var expiredRule = new Event
        {
            Id = "expired-rule",
            Title = "已結束祭典",
            StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 1, 1, 3, 0, 0, DateTimeKind.Utc),
            IsLunarDate = true,
            LunarDate = new LunarDate { Year = 2025, Month = 1, Day = 1 },
            RecurrenceRule = new RecurrenceRule
            {
                Type = RecurrenceType.LunarYearly,
                EffectiveUntil = effectiveUntil
            },
            CreatedBy = "creator-1"
        };
        var recurrenceService = new FakeLunarRecurrenceService
        {
            OccurrenceFactory = rule =>
            [
                OccurrenceFor(
                    rule.Id,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))
            ]
        };
        var service = new EventService(
            new FakeEventRepository { RecurringRules = [finalRule, expiredRule] },
            new FakeCalendarNotifier(),
            recurrenceService);

        var result = await service.GetByDateRangeAsync(
            new EventRangeRequest
            {
                Start = new DateTime(2026, 1, 2, 0, 30, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc)
            },
            CancellationToken.None);

        var responses = Assert.IsAssignableFrom<IReadOnlyList<EventResponse>>(result.Value);
        var only = Assert.Single(responses);
        Assert.Equal("final-overnight-rule:2026-01-01", only.Id);
        Assert.Equal(new DateTime(2026, 1, 1, 23, 0, 0, DateTimeKind.Utc), only.StartDate);
        Assert.Equal(new DateTime(2026, 1, 2, 2, 0, 0, DateTimeKind.Utc), only.EndDate);
        Assert.DoesNotContain(responses, response => response.SourceRuleId == "expired-rule");
    }

    /// <summary>舊資料若含負期間，不得因此把 recurrence 展開起點錯誤向前移動。</summary>
    [Fact]
    public async Task GetByDateRangeAsync_DoesNotExpandBackForNegativeSourceDuration()
    {
        var rule = new Event
        {
            Id = "invalid-duration-rule",
            Title = "負期間舊資料",
            StartDate = new DateTime(2025, 1, 2, 2, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 1, 2, 1, 0, 0, DateTimeKind.Utc),
            IsLunarDate = true,
            LunarDate = new LunarDate { Year = 2025, Month = 1, Day = 2 },
            RecurrenceRule = new RecurrenceRule { Type = RecurrenceType.LunarYearly },
            CreatedBy = "creator-1"
        };
        var recurrenceService = new FakeLunarRecurrenceService
        {
            Occurrences =
            [
                OccurrenceFor(
                    rule.Id,
                    new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc))
            ]
        };
        var service = new EventService(
            new FakeEventRepository { RecurringRules = [rule] },
            new FakeCalendarNotifier(),
            recurrenceService);

        await service.GetByDateRangeAsync(
            new EventRangeRequest
            {
                Start = new DateTime(2026, 1, 2, 0, 30, 0, DateTimeKind.Utc),
                End = new DateTime(2026, 1, 2, 23, 0, 0, DateTimeKind.Utc)
            },
            CancellationToken.None);

        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), recurrenceService.ExpansionRequests[0].Start);
    }

    /// <summary>讀取事件時應複製農曆資料並忽略資料庫內不可信的顯示文字。</summary>
    [Fact]
    public async Task GetByIdAsync_RebuildsCanonicalLunarDisplayTextWithoutSharingEntityObject()
    {
        var repository = new FakeEventRepository
        {
            Stored = new Event
            {
                Id = "event-1",
                Title = "農曆事件",
                CreatedBy = "creator-1",
                LunarDate = new LunarDate
                {
                    Year = 2026,
                    Month = 1,
                    Day = 1,
                    DisplayText = "偽造文字"
                }
            }
        };
        var service = new EventService(repository, new FakeCalendarNotifier(), new FakeLunarRecurrenceService());

        var result = await service.GetByIdAsync("event-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("農曆正月初一", result.Value!.LunarDate?.DisplayText);
        Assert.NotSame(repository.Stored.LunarDate, result.Value.LunarDate);
    }

    /// <summary>讀取非法舊資料時應使用固定安全文字且不得因陣列索引而失敗。</summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(13, 1)]
    [InlineData(1, 0)]
    [InlineData(1, 31)]
    public async Task GetByIdAsync_InvalidLegacyLunarDateUsesDeterministicFallback(int month, int day)
    {
        var repository = new FakeEventRepository
        {
            Stored = new Event
            {
                Id = "event-1",
                Title = "舊資料",
                CreatedBy = "creator-1",
                LunarDate = new LunarDate
                {
                    Year = 2026,
                    Month = month,
                    Day = day,
                    DisplayText = "不可採信"
                }
            }
        };
        var service = new EventService(repository, new FakeCalendarNotifier(), new FakeLunarRecurrenceService());

        var result = await service.GetByIdAsync("event-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("農曆日期無效", result.Value!.LunarDate?.DisplayText);
    }

    /// <summary>所有事件清單查詢都應使用中央定義的最大筆數。</summary>
    [Fact]
    public async Task ListQueries_RequestExactlyMaximumPageSize()
    {
        var repository = new FakeEventRepository();
        var service = new EventService(repository, new FakeCalendarNotifier(), new FakeLunarRecurrenceService());

        await service.GetByTypeAsync(nameof(EventType.General), CancellationToken.None);
        await service.GetByLunarDateAsync(1, 1, CancellationToken.None);

        Assert.Equal(ValidationLimits.MaxPageSize, repository.LastTypeLimit);
        Assert.Equal(ValidationLimits.MaxPageSize, repository.LastLunarLimit);
    }

    private static UpsertEventRequest ValidRequest() => new()
    {
        Title = "家族聚會",
        EventType = nameof(EventType.General),
        StartDate = new DateTime(2026, 2, 17, 1, 0, 0, DateTimeKind.Utc),
        EndDate = new DateTime(2026, 2, 17, 2, 0, 0, DateTimeKind.Utc),
        RelatedMemberIds = ["member-1"],
        Tags = ["family"]
    };

    private static LunarOccurrence OccurrenceFor(string ruleId, DateTime solarDate) => new()
    {
        Id = $"{ruleId}:{solarDate:yyyy-MM-dd}",
        SourceRuleId = ruleId,
        SolarDate = solarDate,
        LunarYear = 2026,
        LunarMonth = 1,
        LunarDay = 1
    };

    private sealed class FakeCalendarNotifier : ICalendarNotifier
    {
        public (string Name, string Id)? LastEvent { get; private set; }

        /// <inheritdoc />
        public Task EventChangedAsync(
            string eventName,
            EventResponse response,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastEvent = (eventName, response.Id);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeEventRepository : IEventRepository
    {
        public Event? Stored { get; init; }
        public Event? Created { get; private set; }
        public Event? Updated { get; private set; }
        public string? DeletedId { get; private set; }
        public (DateTime Start, DateTime End)? LastRange { get; private set; }
        public int LastRangeLimit { get; private set; }
        public int LastTypeLimit { get; private set; }
        public int LastLunarLimit { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }
        public List<Event> RangeEvents { get; init; } = [];
        public List<Event> RecurringRules { get; init; } = [];
        public CancellationToken LastRecurringCancellationToken { get; private set; }

        /// <inheritdoc />
        public Task<Event?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(Stored?.Id == id ? Stored : null);
        }

        /// <inheritdoc />
        public Task<List<Event>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<Event>());

        /// <inheritdoc />
        public Task<List<Event>> FindAsync(
            Expression<Func<Event, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<Event>());

        /// <inheritdoc />
        public Task<Event?> FindOneAsync(
            Expression<Func<Event, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult<Event?>(null);

        /// <inheritdoc />
        public Task CreateAsync(Event entity, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            Created = entity;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpdateAsync(string id, Event entity, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            Updated = entity;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            DeletedId = id;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(
            Expression<Func<Event, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult(false);

        /// <inheritdoc />
        public Task<long> CountAsync(
            Expression<Func<Event, bool>>? filter = null,
            CancellationToken cancellationToken = default) => Task.FromResult(0L);

        /// <inheritdoc />
        public Task<List<Event>> GetByDateRangeAsync(
            DateTime start,
            DateTime end,
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastRange = (start, end);
            LastRangeLimit = limit;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(RangeEvents);
        }

        /// <inheritdoc />
        public Task<List<Event>> GetRecurringRulesAsync(
            DateTime start,
            DateTime end,
            CancellationToken cancellationToken = default)
        {
            LastRecurringCancellationToken = cancellationToken;
            return Task.FromResult(RecurringRules);
        }

        /// <inheritdoc />
        public Task<List<Event>> GetByEventTypeAsync(
            EventType eventType,
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastTypeLimit = limit;
            return Task.FromResult(new List<Event>());
        }

        /// <inheritdoc />
        public Task<List<Event>> GetByTagAsync(
            string tag,
            int limit,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<Event>());

        /// <inheritdoc />
        public Task<List<Event>> GetByLunarDateAsync(
            int month,
            int day,
            int limit,
            CancellationToken cancellationToken = default)
        {
            LastLunarLimit = limit;
            return Task.FromResult(new List<Event>());
        }
    }

    private sealed class FakeLunarRecurrenceService : ILunarRecurrenceService
    {
        public IReadOnlyList<LunarOccurrence> Occurrences { get; init; } = [];
        public Func<Event, IReadOnlyList<LunarOccurrence>>? OccurrenceFactory { get; init; }
        public List<(string RuleId, DateTime Start, DateTime End)> ExpansionRequests { get; } = [];

        /// <inheritdoc />
        public Task<IReadOnlyList<LunarOccurrence>> ExpandAsync(
            Event rule,
            DateTime rangeStart,
            DateTime rangeEnd,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExpansionRequests.Add((rule.Id, rangeStart, rangeEnd));
            var candidates = OccurrenceFactory?.Invoke(rule) ?? Occurrences;
            IReadOnlyList<LunarOccurrence> result = candidates
                .Where(occurrence => occurrence.SolarDate >= rangeStart && occurrence.SolarDate <= rangeEnd)
                .ToList();
            return Task.FromResult(result);
        }
    }
}
