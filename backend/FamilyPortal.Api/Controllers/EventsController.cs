using System.Security.Claims;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Event;
using FamilyPortal.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyPortal.Api.Controllers;

/// <summary>行事曆事件 HTTP adapter。</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EventsController(IEventService eventService) : ControllerBase
{
    /// <summary>GET /api/events?start=...&amp;end=... — 根據日期範圍取得事件。</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<EventResponse>>>> GetByDateRange(
        [FromQuery] EventRangeRequest request,
        CancellationToken cancellationToken)
    {
        var result = await eventService.GetByDateRangeAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<List<EventResponse>>.Ok([.. result.Value!]))
            : ToListActionResult(result);
    }

    /// <summary>GET /api/events/{id} — 取得單一事件。</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<EventResponse>>> GetById(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await eventService.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<EventResponse>.Ok(result.Value!))
            : ToActionResult(result);
    }

    /// <summary>GET /api/events/type/{eventType} — 根據類型取得事件。</summary>
    [HttpGet("type/{eventType}")]
    public async Task<ActionResult<ApiResponse<List<EventResponse>>>> GetByType(
        string eventType,
        CancellationToken cancellationToken)
    {
        var result = await eventService.GetByTypeAsync(eventType, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<List<EventResponse>>.Ok([.. result.Value!]))
            : ToListActionResult(result);
    }

    /// <summary>GET /api/events/lunar?month=...&amp;day=... — 根據農曆日期取得事件。</summary>
    [HttpGet("lunar")]
    public async Task<ActionResult<ApiResponse<List<EventResponse>>>> GetByLunarDate(
        [FromQuery] int month,
        [FromQuery] int day,
        CancellationToken cancellationToken)
    {
        var result = await eventService.GetByLunarDateAsync(month, day, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<List<EventResponse>>.Ok([.. result.Value!]))
            : ToListActionResult(result);
    }

    /// <summary>POST /api/events — 新增事件（僅 Admin），並推播行事曆變更。</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<EventResponse>>> Create(
        [FromBody] UpsertEventRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiResponse<EventResponse>.Fail("未授權"));
        }

        var result = await eventService.CreateAsync(request, userId, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(
                nameof(GetById),
                new { id = result.Value!.Id },
                ApiResponse<EventResponse>.Ok(result.Value, "事件新增成功"))
            : ToActionResult(result);
    }

    /// <summary>PUT /api/events/{id} — 更新事件（僅 Admin），並推播行事曆變更。</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<EventResponse>>> Update(
        string id,
        [FromBody] UpsertEventRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiResponse<EventResponse>.Fail("未授權"));
        }

        var result = await eventService.UpdateAsync(id, request, userId, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<EventResponse>.Ok(result.Value!, "事件更新成功"))
            : ToActionResult(result);
    }

    /// <summary>DELETE /api/events/{id} — 刪除事件（僅 Admin），並推播行事曆變更。</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse>> Delete(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await eventService.DeleteAsync(id, cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(ApiResponse.Ok("事件刪除成功"));
        }

        return result.Error switch
        {
            ServiceError.Validation => BadRequest(ApiResponse.Fail(result.Message ?? "驗證失敗")),
            ServiceError.Unauthorized => Unauthorized(ApiResponse.Fail(result.Message ?? "未授權")),
            ServiceError.Forbidden => StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse.Fail(result.Message ?? "禁止存取")),
            ServiceError.NotFound => NotFound(ApiResponse.Fail(result.Message ?? "事件不存在")),
            ServiceError.Conflict => Conflict(ApiResponse.Fail(result.Message ?? "資料衝突")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse.Fail("事件操作失敗"))
        };
    }

    private ActionResult<ApiResponse<EventResponse>> ToActionResult(
        ServiceResult<EventResponse> result) => result.Error switch
    {
        ServiceError.Validation => BadRequest(ApiResponse<EventResponse>.Fail(result.Message ?? "驗證失敗")),
        ServiceError.Unauthorized => Unauthorized(ApiResponse<EventResponse>.Fail(result.Message ?? "未授權")),
        ServiceError.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ApiResponse<EventResponse>.Fail(result.Message ?? "禁止存取")),
        ServiceError.NotFound => NotFound(ApiResponse<EventResponse>.Fail(result.Message ?? "事件不存在")),
        ServiceError.Conflict => Conflict(ApiResponse<EventResponse>.Fail(result.Message ?? "資料衝突")),
        _ => StatusCode(
            StatusCodes.Status500InternalServerError,
            ApiResponse<EventResponse>.Fail("事件操作失敗"))
    };

    private ActionResult<ApiResponse<List<EventResponse>>> ToListActionResult(
        ServiceResult<IReadOnlyList<EventResponse>> result) => result.Error switch
    {
        ServiceError.Validation => BadRequest(ApiResponse<List<EventResponse>>.Fail(result.Message ?? "驗證失敗")),
        ServiceError.Unauthorized => Unauthorized(ApiResponse<List<EventResponse>>.Fail(result.Message ?? "未授權")),
        ServiceError.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ApiResponse<List<EventResponse>>.Fail(result.Message ?? "禁止存取")),
        ServiceError.NotFound => NotFound(ApiResponse<List<EventResponse>>.Fail(result.Message ?? "事件不存在")),
        ServiceError.Conflict => Conflict(ApiResponse<List<EventResponse>>.Fail(result.Message ?? "資料衝突")),
        _ => StatusCode(
            StatusCodes.Status500InternalServerError,
            ApiResponse<List<EventResponse>>.Fail("事件操作失敗"))
    };
}
