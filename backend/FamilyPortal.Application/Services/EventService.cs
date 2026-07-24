using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Application.DTOs.Event;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Domain.ValueObjects;
using DomainEvent = FamilyPortal.Domain.Entities.Event;

namespace FamilyPortal.Application.Services;

/// <summary>協調事件資料存取、輸入正規化與行事曆通知。</summary>
public sealed class EventService(
    IEventRepository eventRepository,
    ICalendarNotifier calendarNotifier) : IEventService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<EventResponse>>> GetByDateRangeAsync(
        EventRangeRequest request,
        CancellationToken cancellationToken)
    {
        var events = await eventRepository.GetByDateRangeAsync(
            NormalizeUtc(request.Start),
            NormalizeUtc(request.End),
            cancellationToken);

        return ServiceResult<IReadOnlyList<EventResponse>>.Success(
            events.Select(MapToResponse).ToList());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<EventResponse>> GetByIdAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var evt = await eventRepository.GetByIdAsync(id, cancellationToken);
        return evt is null
            ? ServiceResult<EventResponse>.Failure(ServiceError.NotFound, "事件不存在")
            : ServiceResult<EventResponse>.Success(MapToResponse(evt));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<EventResponse>>> GetByTypeAsync(
        string eventType,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<EventType>(eventType, true, out var parsedType) ||
            !Enum.IsDefined(parsedType))
        {
            return ServiceResult<IReadOnlyList<EventResponse>>.Failure(
                ServiceError.Validation,
                "無效的事件類型");
        }

        var events = await eventRepository.GetByEventTypeAsync(parsedType, cancellationToken);
        return ServiceResult<IReadOnlyList<EventResponse>>.Success(
            events.Select(MapToResponse).ToList());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<EventResponse>>> GetByLunarDateAsync(
        int month,
        int day,
        CancellationToken cancellationToken)
    {
        if (month is < 1 or > 12 || day is < 1 or > 30)
        {
            return ServiceResult<IReadOnlyList<EventResponse>>.Failure(
                ServiceError.Validation,
                "無效的農曆日期");
        }

        var events = await eventRepository.GetByLunarDateAsync(month, day, cancellationToken);
        return ServiceResult<IReadOnlyList<EventResponse>>.Success(
            events.Select(MapToResponse).ToList());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<EventResponse>> CreateAsync(
        UpsertEventRequest request,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<EventResponse>.Failure(ServiceError.Unauthorized, "未授權");
        }

        if (!TryParseEventType(request.EventType, out var eventType))
        {
            return ServiceResult<EventResponse>.Failure(ServiceError.Validation, "無效的事件類型");
        }

        var evt = MapRequest(request, eventType);
        evt.CreatedBy = userId;

        await eventRepository.CreateAsync(evt, cancellationToken);
        var response = MapToResponse(evt);
        await calendarNotifier.EventChangedAsync("EventCreated", response, cancellationToken);

        return ServiceResult<EventResponse>.Success(response);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<EventResponse>> UpdateAsync(
        string id,
        UpsertEventRequest request,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<EventResponse>.Failure(ServiceError.Unauthorized, "未授權");
        }

        if (!TryParseEventType(request.EventType, out var eventType))
        {
            return ServiceResult<EventResponse>.Failure(ServiceError.Validation, "無效的事件類型");
        }

        var existing = await eventRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return ServiceResult<EventResponse>.Failure(ServiceError.NotFound, "事件不存在");
        }

        var updated = MapRequest(request, eventType);
        updated.Id = existing.Id;
        updated.CreatedBy = existing.CreatedBy;
        updated.CreatedAt = existing.CreatedAt;
        updated.UpdatedAt = DateTime.UtcNow;

        await eventRepository.UpdateAsync(id, updated, cancellationToken);
        var response = MapToResponse(updated);
        await calendarNotifier.EventChangedAsync("EventUpdated", response, cancellationToken);

        return ServiceResult<EventResponse>.Success(response);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var existing = await eventRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return ServiceResult<bool>.Failure(ServiceError.NotFound, "事件不存在");
        }

        await eventRepository.DeleteAsync(id, cancellationToken);
        await calendarNotifier.EventChangedAsync(
            "EventDeleted",
            MapToResponse(existing),
            cancellationToken);

        return ServiceResult<bool>.Success(true);
    }

    private static bool TryParseEventType(string value, out EventType eventType) =>
        Enum.TryParse(value, true, out eventType) && Enum.IsDefined(eventType);

    private static DomainEvent MapRequest(UpsertEventRequest request, EventType eventType) => new()
    {
        Title = request.Title,
        Description = request.Description,
        EventType = eventType,
        StartDate = NormalizeUtc(request.StartDate),
        EndDate = request.EndDate is { } endDate ? NormalizeUtc(endDate) : null,
        IsAllDay = request.IsAllDay,
        IsLunarDate = request.IsLunarDate,
        LunarDate = MapLunarDate(request.LunarDate),
        RecurrenceRule = MapRecurrenceRule(request.RecurrenceRule),
        WorshipDetails = MapWorshipDetails(request.WorshipDetails),
        RelatedMemberIds = [.. request.RelatedMemberIds],
        Tags = [.. request.Tags],
        Color = request.Color
    };

    private static LunarDate? MapLunarDate(LunarDateRequest? request) =>
        request is null
            ? null
            : new LunarDate
            {
                Year = request.Year,
                Month = request.Month,
                Day = request.Day,
                IsLeapMonth = request.IsLeapMonth,
                DisplayText = FormatLunarDate(request.Month, request.Day, request.IsLeapMonth)
            };

    private static RecurrenceRule? MapRecurrenceRule(RecurrenceRuleRequest? request) =>
        request is null
            ? null
            : new RecurrenceRule
            {
                Type = request.Type,
                Interval = request.Interval,
                LunarDays = request.LunarDays is null ? null : [.. request.LunarDays]
            };

    private static WorshipDetails? MapWorshipDetails(WorshipDetailsRequest? request) =>
        request is null
            ? null
            : new WorshipDetails
            {
                Deity = request.Deity,
                Offerings = [.. request.Offerings],
                Preparations = [.. request.Preparations],
                Notes = request.Notes
            };

    private static EventResponse MapToResponse(DomainEvent evt) => new()
    {
        Id = evt.Id,
        Title = evt.Title,
        Description = evt.Description,
        EventType = evt.EventType.ToString(),
        StartDate = evt.StartDate,
        EndDate = evt.EndDate,
        IsAllDay = evt.IsAllDay,
        IsLunarDate = evt.IsLunarDate,
        LunarDate = evt.LunarDate,
        RecurrenceRule = evt.RecurrenceRule,
        WorshipDetails = evt.WorshipDetails,
        RelatedMemberIds = [.. evt.RelatedMemberIds],
        Tags = [.. evt.Tags],
        Color = evt.Color,
        CreatedBy = evt.CreatedBy,
        CreatedAt = evt.CreatedAt,
        UpdatedAt = evt.UpdatedAt
    };

    private static DateTime NormalizeUtc(DateTime value) => value.ToUniversalTime();

    private static string FormatLunarDate(int month, int day, bool isLeapMonth)
    {
        string[] monthNames = ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "臘"];
        string[] dayNames =
        [
            "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
            "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
            "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
        ];

        var leapText = isLeapMonth ? "閏" : string.Empty;
        return $"農曆{leapText}{monthNames[month - 1]}月{dayNames[day - 1]}";
    }
}
