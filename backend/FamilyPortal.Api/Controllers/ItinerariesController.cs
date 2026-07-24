using System.Security.Claims;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Itinerary;
using FamilyPortal.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyPortal.Api.Controllers;

/// <summary>行程紀錄 HTTP adapter。</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ItinerariesController(IItineraryService itineraryService) : ControllerBase
{
    /// <summary>GET /api/itineraries — 分頁查詢行程紀錄（游標式分頁）。</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ItineraryResponse>>>> GetPaged(
        [FromQuery] PagedItineraryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await itineraryService.GetPagedAsync(request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<List<ItineraryResponse>>.Ok([.. result.Value!]))
            : ToListActionResult(result);
    }

    /// <summary>GET /api/itineraries/{id} — 取得單一行程。</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ItineraryResponse>>> GetById(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await itineraryService.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ItineraryResponse>.Ok(result.Value!))
            : ToActionResult(result);
    }

    /// <summary>GET /api/itineraries/search?keyword=... — 全文搜尋行程。</summary>
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<List<ItineraryResponse>>>> Search(
        [FromQuery] string keyword,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await itineraryService.SearchAsync(keyword, limit, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<List<ItineraryResponse>>.Ok([.. result.Value!]))
            : ToListActionResult(result);
    }

    /// <summary>GET /api/itineraries/participant/{memberId} — 查詢成員參與過的行程。</summary>
    [HttpGet("participant/{memberId}")]
    public async Task<ActionResult<ApiResponse<List<ItineraryResponse>>>> GetByParticipant(
        string memberId,
        [FromQuery] int limit = ValidationLimits.MaxPageSize,
        CancellationToken cancellationToken = default)
    {
        var result = await itineraryService.GetByParticipantAsync(
            memberId,
            limit,
            cancellationToken);

        return result.IsSuccess
            ? Ok(ApiResponse<List<ItineraryResponse>>.Ok([.. result.Value!]))
            : ToListActionResult(result);
    }

    /// <summary>POST /api/itineraries — 新增行程紀錄（僅 Admin）。</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<ItineraryResponse>>> Create(
        [FromBody] UpsertItineraryRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiResponse<ItineraryResponse>.Fail("未授權"));
        }

        var result = await itineraryService.CreateAsync(request, userId, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(
                nameof(GetById),
                new { id = result.Value!.Id },
                ApiResponse<ItineraryResponse>.Ok(result.Value, "行程新增成功"))
            : ToActionResult(result);
    }

    /// <summary>PUT /api/itineraries/{id} — 更新行程紀錄（僅 Admin）。</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<ItineraryResponse>>> Update(
        string id,
        [FromBody] UpsertItineraryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await itineraryService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<ItineraryResponse>.Ok(result.Value!, "行程更新成功"))
            : ToActionResult(result);
    }

    /// <summary>DELETE /api/itineraries/{id} — 刪除行程紀錄（僅 Admin）。</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse>> Delete(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await itineraryService.DeleteAsync(id, cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(ApiResponse.Ok("行程刪除成功"));
        }

        return result.Error switch
        {
            ServiceError.Validation => BadRequest(ApiResponse.Fail(result.Message ?? "驗證失敗")),
            ServiceError.Unauthorized => Unauthorized(ApiResponse.Fail(result.Message ?? "未授權")),
            ServiceError.Forbidden => StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse.Fail(result.Message ?? "禁止存取")),
            ServiceError.NotFound => NotFound(ApiResponse.Fail(result.Message ?? "行程不存在")),
            ServiceError.Conflict => Conflict(ApiResponse.Fail(result.Message ?? "資料衝突")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse.Fail("行程操作失敗"))
        };
    }

    private ActionResult<ApiResponse<ItineraryResponse>> ToActionResult(
        ServiceResult<ItineraryResponse> result) => result.Error switch
    {
        ServiceError.Validation => BadRequest(ApiResponse<ItineraryResponse>.Fail(result.Message ?? "驗證失敗")),
        ServiceError.Unauthorized => Unauthorized(ApiResponse<ItineraryResponse>.Fail(result.Message ?? "未授權")),
        ServiceError.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ApiResponse<ItineraryResponse>.Fail(result.Message ?? "禁止存取")),
        ServiceError.NotFound => NotFound(ApiResponse<ItineraryResponse>.Fail(result.Message ?? "行程不存在")),
        ServiceError.Conflict => Conflict(ApiResponse<ItineraryResponse>.Fail(result.Message ?? "資料衝突")),
        _ => StatusCode(
            StatusCodes.Status500InternalServerError,
            ApiResponse<ItineraryResponse>.Fail("行程操作失敗"))
    };

    private ActionResult<ApiResponse<List<ItineraryResponse>>> ToListActionResult(
        ServiceResult<IReadOnlyList<ItineraryResponse>> result) => result.Error switch
    {
        ServiceError.Validation => BadRequest(ApiResponse<List<ItineraryResponse>>.Fail(result.Message ?? "驗證失敗")),
        ServiceError.Unauthorized => Unauthorized(ApiResponse<List<ItineraryResponse>>.Fail(result.Message ?? "未授權")),
        ServiceError.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ApiResponse<List<ItineraryResponse>>.Fail(result.Message ?? "禁止存取")),
        ServiceError.NotFound => NotFound(ApiResponse<List<ItineraryResponse>>.Fail(result.Message ?? "行程不存在")),
        ServiceError.Conflict => Conflict(ApiResponse<List<ItineraryResponse>>.Fail(result.Message ?? "資料衝突")),
        _ => StatusCode(
            StatusCodes.Status500InternalServerError,
            ApiResponse<List<ItineraryResponse>>.Fail("行程操作失敗"))
    };
}
