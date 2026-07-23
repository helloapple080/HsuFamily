using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Event;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Infrastructure.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace FamilyPortal.Api.Controllers;

/// <summary>
/// 行事曆事件控制器 — 事件 CRUD 與 SignalR 即時推播
/// 寫入操作完成後自動透過 SignalR 通知所有在線用戶
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EventsController : ControllerBase
{
    private readonly IEventRepository _eventRepo;
    private readonly IHubContext<CalendarHub> _calendarHub;
    private readonly ILogger<EventsController> _logger;

    public EventsController(
        IEventRepository eventRepo,
        IHubContext<CalendarHub> calendarHub,
        ILogger<EventsController> logger)
    {
        _eventRepo = eventRepo;
        _calendarHub = calendarHub;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/events?start=...&end=... — 根據日期範圍取得事件（行事曆月檢視）
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<EventResponse>>>> GetByDateRange(
        [FromQuery] DateTime start,
        [FromQuery] DateTime end)
    {
        var events = await _eventRepo.GetByDateRangeAsync(start, end);
        var response = events.Select(MapToResponse).ToList();
        return Ok(ApiResponse<List<EventResponse>>.Ok(response));
    }

    /// <summary>
    /// GET /api/events/{id} — 取得單一事件
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<EventResponse>>> GetById(string id)
    {
        var evt = await _eventRepo.GetByIdAsync(id);
        if (evt == null)
        {
            return NotFound(ApiResponse<EventResponse>.Fail("事件不存在"));
        }

        return Ok(ApiResponse<EventResponse>.Ok(MapToResponse(evt)));
    }

    /// <summary>
    /// GET /api/events/type/{eventType} — 根據類型取得事件
    /// </summary>
    [HttpGet("type/{eventType}")]
    public async Task<ActionResult<ApiResponse<List<EventResponse>>>> GetByType(string eventType)
    {
        if (!Enum.TryParse<EventType>(eventType, true, out var type))
        {
            return BadRequest(ApiResponse<List<EventResponse>>.Fail("無效的事件類型"));
        }

        var events = await _eventRepo.GetByEventTypeAsync(type);
        var response = events.Select(MapToResponse).ToList();
        return Ok(ApiResponse<List<EventResponse>>.Ok(response));
    }

    /// <summary>
    /// GET /api/events/lunar?month=...&day=... — 根據農曆日期取得事件
    /// </summary>
    [HttpGet("lunar")]
    public async Task<ActionResult<ApiResponse<List<EventResponse>>>> GetByLunarDate(
        [FromQuery] int month, [FromQuery] int day)
    {
        var events = await _eventRepo.GetByLunarDateAsync(month, day);
        var response = events.Select(MapToResponse).ToList();
        return Ok(ApiResponse<List<EventResponse>>.Ok(response));
    }

    /// <summary>
    /// POST /api/events — 新增事件（僅 Admin），並透過 SignalR 推播
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<EventResponse>>> Create([FromBody] UpsertEventRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Unauthorized(ApiResponse<EventResponse>.Fail("未授權"));

        if (!Enum.TryParse<EventType>(request.EventType, true, out var eventType))
        {
            return BadRequest(ApiResponse<EventResponse>.Fail("無效的事件類型"));
        }

        var evt = new Domain.Entities.Event
        {
            Title = request.Title,
            Description = request.Description,
            EventType = eventType,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            IsAllDay = request.IsAllDay,
            IsLunarDate = request.IsLunarDate,
            LunarDate = request.LunarDate,
            RecurrenceRule = request.RecurrenceRule,
            WorshipDetails = request.WorshipDetails,
            RelatedMemberIds = request.RelatedMemberIds,
            Tags = request.Tags,
            Color = request.Color,
            CreatedBy = userId
        };

        await _eventRepo.CreateAsync(evt);
        _logger.LogInformation("新增事件：{EventId}", evt.Id);

        // 🔔 SignalR 即時推播 — 通知所有在線用戶
        await _calendarHub.Clients.Group("FamilyCalendar")
            .SendAsync("EventCreated", MapToResponse(evt));

        return CreatedAtAction(
            nameof(GetById),
            new { id = evt.Id },
            ApiResponse<EventResponse>.Ok(MapToResponse(evt), "事件新增成功"));
    }

    /// <summary>
    /// PUT /api/events/{id} — 更新事件（僅 Admin），並透過 SignalR 推播
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<EventResponse>>> Update(
        string id, [FromBody] UpsertEventRequest request)
    {
        var evt = await _eventRepo.GetByIdAsync(id);
        if (evt == null)
        {
            return NotFound(ApiResponse<EventResponse>.Fail("事件不存在"));
        }

        if (!Enum.TryParse<EventType>(request.EventType, true, out var eventType))
        {
            return BadRequest(ApiResponse<EventResponse>.Fail("無效的事件類型"));
        }

        evt.Title = request.Title;
        evt.Description = request.Description;
        evt.EventType = eventType;
        evt.StartDate = request.StartDate;
        evt.EndDate = request.EndDate;
        evt.IsAllDay = request.IsAllDay;
        evt.IsLunarDate = request.IsLunarDate;
        evt.LunarDate = request.LunarDate;
        evt.RecurrenceRule = request.RecurrenceRule;
        evt.WorshipDetails = request.WorshipDetails;
        evt.RelatedMemberIds = request.RelatedMemberIds;
        evt.Tags = request.Tags;
        evt.Color = request.Color;

        await _eventRepo.UpdateAsync(id, evt);
        _logger.LogInformation("更新事件：{EventId}", evt.Id);

        // 🔔 SignalR 即時推播
        await _calendarHub.Clients.Group("FamilyCalendar")
            .SendAsync("EventUpdated", MapToResponse(evt));

        return Ok(ApiResponse<EventResponse>.Ok(MapToResponse(evt), "事件更新成功"));
    }

    /// <summary>
    /// DELETE /api/events/{id} — 刪除事件（僅 Admin），並透過 SignalR 推播
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse>> Delete(string id)
    {
        var evt = await _eventRepo.GetByIdAsync(id);
        if (evt == null)
        {
            return NotFound(ApiResponse.Fail("事件不存在"));
        }

        await _eventRepo.DeleteAsync(id);
        _logger.LogInformation("刪除事件：{EventId}", evt.Id);

        // 🔔 SignalR 即時推播
        await _calendarHub.Clients.Group("FamilyCalendar")
            .SendAsync("EventDeleted", new { id });

        return Ok(ApiResponse.Ok("事件刪除成功"));
    }

    /// <summary>Entity 轉 DTO 映射</summary>
    private static EventResponse MapToResponse(Domain.Entities.Event evt) => new()
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
        RelatedMemberIds = evt.RelatedMemberIds,
        Tags = evt.Tags,
        Color = evt.Color,
        CreatedBy = evt.CreatedBy,
        CreatedAt = evt.CreatedAt,
        UpdatedAt = evt.UpdatedAt
    };
}
