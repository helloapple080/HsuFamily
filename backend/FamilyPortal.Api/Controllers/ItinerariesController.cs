using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Application.DTOs.Itinerary;
using FamilyPortal.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyPortal.Api.Controllers;

/// <summary>
/// 行程紀錄控制器 — 歷年行程的 CRUD 與分頁查詢
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ItinerariesController : ControllerBase
{
    private readonly IItineraryRepository _itineraryRepo;
    private readonly ILogger<ItinerariesController> _logger;

    public ItinerariesController(
        IItineraryRepository itineraryRepo,
        ILogger<ItinerariesController> logger)
    {
        _itineraryRepo = itineraryRepo;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/itineraries — 分頁查詢行程紀錄（游標式分頁）
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ItineraryResponse>>>> GetPaged(
        [FromQuery] PagedItineraryRequest request)
    {
        var itineraries = await _itineraryRepo.GetPagedAsync(
            request.Tag,
            request.CursorDate,
            request.CursorId,
            request.PageSize);

        var response = itineraries.Select(MapToResponse).ToList();

        return Ok(ApiResponse<List<ItineraryResponse>>.Ok(response));
    }

    /// <summary>
    /// GET /api/itineraries/{id} — 取得單一行程
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<ItineraryResponse>>> GetById(string id)
    {
        var itinerary = await _itineraryRepo.GetByIdAsync(id);
        if (itinerary == null)
        {
            return NotFound(ApiResponse<ItineraryResponse>.Fail("行程不存在"));
        }

        return Ok(ApiResponse<ItineraryResponse>.Ok(MapToResponse(itinerary)));
    }

    /// <summary>
    /// GET /api/itineraries/search?keyword=... — 全文搜尋行程
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<ApiResponse<List<ItineraryResponse>>>> Search(
        [FromQuery] string keyword, [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return BadRequest(ApiResponse<List<ItineraryResponse>>.Fail("搜尋關鍵字不可為空"));
        }

        var itineraries = await _itineraryRepo.SearchAsync(keyword, limit);
        var response = itineraries.Select(MapToResponse).ToList();
        return Ok(ApiResponse<List<ItineraryResponse>>.Ok(response));
    }

    /// <summary>
    /// GET /api/itineraries/participant/{memberId} — 查詢成員參與過的行程
    /// </summary>
    [HttpGet("participant/{memberId}")]
    public async Task<ActionResult<ApiResponse<List<ItineraryResponse>>>> GetByParticipant(
        string memberId)
    {
        var itineraries = await _itineraryRepo.GetByParticipantAsync(memberId);
        var response = itineraries.Select(MapToResponse).ToList();
        return Ok(ApiResponse<List<ItineraryResponse>>.Ok(response));
    }

    /// <summary>
    /// POST /api/itineraries — 新增行程紀錄（僅 Admin）
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<ItineraryResponse>>> Create(
        [FromBody] UpsertItineraryRequest request)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (userId == null) return Unauthorized(ApiResponse<ItineraryResponse>.Fail("未授權"));

        var itinerary = new Domain.Entities.Itinerary
        {
            Title = request.Title,
            Description = request.Description,
            Date = request.Date,
            EndDate = request.EndDate,
            Location = request.Location,
            Tags = request.Tags,
            Participants = MapParticipantRequests(request.Participants),
            Photos = request.Photos,
            Notes = request.Notes,
            LinkedEventId = request.LinkedEventId,
            CreatedBy = userId
        };

        await _itineraryRepo.CreateAsync(itinerary);
        _logger.LogInformation("新增行程：{ItineraryId}", itinerary.Id);

        return CreatedAtAction(
            nameof(GetById),
            new { id = itinerary.Id },
            ApiResponse<ItineraryResponse>.Ok(MapToResponse(itinerary), "行程新增成功"));
    }

    /// <summary>
    /// PUT /api/itineraries/{id} — 更新行程紀錄（僅 Admin）
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<ItineraryResponse>>> Update(
        string id, [FromBody] UpsertItineraryRequest request)
    {
        var itinerary = await _itineraryRepo.GetByIdAsync(id);
        if (itinerary == null)
        {
            return NotFound(ApiResponse<ItineraryResponse>.Fail("行程不存在"));
        }

        itinerary.Title = request.Title;
        itinerary.Description = request.Description;
        itinerary.Date = request.Date;
        itinerary.EndDate = request.EndDate;
        itinerary.Location = request.Location;
        itinerary.Tags = request.Tags;
        itinerary.Participants = MapParticipantRequests(request.Participants);
        itinerary.Photos = request.Photos;
        itinerary.Notes = request.Notes;
        itinerary.LinkedEventId = request.LinkedEventId;

        await _itineraryRepo.UpdateAsync(id, itinerary);
        _logger.LogInformation("更新行程：{ItineraryId}", itinerary.Id);

        return Ok(ApiResponse<ItineraryResponse>.Ok(MapToResponse(itinerary), "行程更新成功"));
    }

    /// <summary>
    /// DELETE /api/itineraries/{id} — 刪除行程紀錄（僅 Admin）
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse>> Delete(string id)
    {
        var itinerary = await _itineraryRepo.GetByIdAsync(id);
        if (itinerary == null)
        {
            return NotFound(ApiResponse.Fail("行程不存在"));
        }

        await _itineraryRepo.DeleteAsync(id);
        _logger.LogInformation("刪除行程：{ItineraryId}", itinerary.Id);

        return Ok(ApiResponse.Ok("行程刪除成功"));
    }

    /// <summary>Entity 轉 DTO 映射</summary>
    private static ItineraryResponse MapToResponse(Domain.Entities.Itinerary itinerary) => new()
    {
        Id = itinerary.Id,
        Title = itinerary.Title,
        Description = itinerary.Description,
        Date = itinerary.Date,
        EndDate = itinerary.EndDate,
        Location = itinerary.Location,
        Tags = itinerary.Tags,
        Participants = itinerary.Participants,
        Photos = itinerary.Photos,
        Notes = itinerary.Notes,
        LinkedEventId = itinerary.LinkedEventId,
        CreatedBy = itinerary.CreatedBy,
        CreatedAt = itinerary.CreatedAt,
        UpdatedAt = itinerary.UpdatedAt
    };

    private static List<Domain.ValueObjects.ParticipantSnapshot> MapParticipantRequests(
        IEnumerable<ParticipantRequest> requests) =>
        requests.Select(request => new Domain.ValueObjects.ParticipantSnapshot
        {
            MemberId = request.MemberId,
            Name = request.Name
        }).ToList();
}
