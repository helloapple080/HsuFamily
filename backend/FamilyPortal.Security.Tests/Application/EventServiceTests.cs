using System.Linq.Expressions;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Application.DTOs.Event;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Services;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
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
        var service = new EventService(repository, notifier);
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
        var service = new EventService(repository, notifier);

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
        var service = new EventService(repository, notifier);
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
        var service = new EventService(repository, notifier);
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
        var service = new EventService(repository, notifier);

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
        var service = new EventService(repository, new FakeCalendarNotifier());
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
        Assert.Equal(source.Token, repository.LastCancellationToken);
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
        public CancellationToken LastCancellationToken { get; private set; }

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
            CancellationToken cancellationToken = default)
        {
            LastRange = (start, end);
            LastCancellationToken = cancellationToken;
            return Task.FromResult(new List<Event>());
        }

        /// <inheritdoc />
        public Task<List<Event>> GetByEventTypeAsync(
            EventType eventType,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<Event>());

        /// <inheritdoc />
        public Task<List<Event>> GetByTagAsync(
            string tag,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<Event>());

        /// <inheritdoc />
        public Task<List<Event>> GetByLunarDateAsync(
            int month,
            int day,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<Event>());
    }
}
