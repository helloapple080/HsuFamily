using System.Security.Claims;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Media;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Models;
using FamilyPortal.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FamilyPortal.Api.Controllers;

/// <summary>媒體上傳、內容讀取、可見性與刪除 HTTP adapter。</summary>
[ApiController]
[Route("api/media")]
public sealed class MediaController(IMediaService mediaService) : ControllerBase
{
    /// <summary>POST /api/media — 驗證並上傳靜態影像（僅 Admin）。</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    [EnableRateLimiting("media-upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(ValidationLimits.MaxMediaRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ValidationLimits.MaxMediaRequestBytes)]
    public async Task<ActionResult<ApiResponse<MediaResponse>>> Upload(
        [FromForm] IFormFile file,
        [FromForm] string purpose,
        [FromForm] string ownerEntityId,
        [FromForm] string visibility,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length is <= 0 or > ValidationLimits.MaxMediaBytes ||
            !TryParseEnumName(purpose, out MediaPurpose parsedPurpose) ||
            !TryParseEnumName(visibility, out MediaVisibility parsedVisibility))
        {
            return BadRequest(ApiResponse<MediaResponse>.Fail("驗證失敗"));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiResponse<MediaResponse>.Fail("未授權"));
        }

        await using var content = file.OpenReadStream();
        var result = await mediaService.UploadAsync(
            new UploadMediaRequest(
                parsedPurpose,
                ownerEntityId,
                parsedVisibility,
                new MediaUpload(content, file.FileName, file.ContentType, file.Length)),
            userId,
            cancellationToken);

        return result.IsSuccess
            ? CreatedAtAction(
                nameof(GetContent),
                new { id = result.Value!.Id },
                ApiResponse<MediaResponse>.Ok(result.Value, "媒體上傳成功"))
            : ToMediaActionResult(result);
    }

    /// <summary>GET /api/media/{id}/content — 依媒體可見性讀取內容。</summary>
    [HttpGet("{id}/content")]
    [AllowAnonymous]
    public async Task<IActionResult> GetContent(
        string id,
        CancellationToken cancellationToken)
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        var result = await mediaService.OpenReadAsync(
            id,
            User.Identity?.IsAuthenticated == true,
            cancellationToken);
        if (!result.IsSuccess)
        {
            Response.Headers.CacheControl = "private, no-store";
            return ToContentError(result.Error);
        }

        var content = result.Value!;
        if (content.Visibility is not MediaVisibility.Public and not MediaVisibility.FamilyOnly)
        {
            content.Content.Dispose();
            Response.Headers.CacheControl = "private, no-store";
            return StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse.Fail("禁止存取媒體"));
        }

        Response.ContentLength = content.Length;
        Response.Headers.CacheControl = content.Visibility == MediaVisibility.Public
            ? "public, max-age=300"
            : "private, no-store";
        return File(content.Content, content.ContentType, enableRangeProcessing: false);
    }

    /// <summary>PATCH /api/media/{id}/visibility — 更新可見性（僅 Admin）。</summary>
    [HttpPatch("{id}/visibility")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<MediaResponse>>> UpdateVisibility(
        string id,
        [FromBody] UpdateMediaVisibilityRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null ||
            !TryParseEnumName(request.Visibility, out MediaVisibility parsedVisibility))
        {
            return BadRequest(ApiResponse<MediaResponse>.Fail("無效的媒體可見性"));
        }

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiResponse<MediaResponse>.Fail("未授權"));
        }

        var result = await mediaService.UpdateVisibilityAsync(
            id,
            parsedVisibility,
            userId,
            cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<MediaResponse>.Ok(result.Value!, "媒體可見性已更新"))
            : ToMediaActionResult(result);
    }

    /// <summary>DELETE /api/media/{id} — 軟刪除並嘗試清理儲存物件（僅 Admin）。</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse>> Delete(
        string id,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return Unauthorized(ApiResponse.Fail("未授權"));
        }

        var result = await mediaService.DeleteAsync(id, userId, cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(ApiResponse.Ok("媒體已刪除"));
        }

        return result.Error switch
        {
            ServiceError.Validation => BadRequest(ApiResponse.Fail(result.Message ?? "驗證失敗")),
            ServiceError.Unauthorized => Unauthorized(ApiResponse.Fail("未授權")),
            ServiceError.Forbidden => StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("禁止存取")),
            ServiceError.NotFound => NotFound(ApiResponse.Fail("媒體不存在")),
            ServiceError.Conflict => Conflict(ApiResponse.Fail(result.Message ?? "媒體刪除失敗")),
            _ => StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Fail("媒體刪除失敗"))
        };
    }

    private ActionResult<ApiResponse<MediaResponse>> ToMediaActionResult(
        ServiceResult<MediaResponse> result) => result.Error switch
    {
        ServiceError.Validation => BadRequest(ApiResponse<MediaResponse>.Fail(result.Message ?? "驗證失敗")),
        ServiceError.Unauthorized => Unauthorized(ApiResponse<MediaResponse>.Fail("未授權")),
        ServiceError.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ApiResponse<MediaResponse>.Fail("禁止存取")),
        ServiceError.NotFound => NotFound(ApiResponse<MediaResponse>.Fail("媒體不存在")),
        ServiceError.Conflict => Conflict(ApiResponse<MediaResponse>.Fail(result.Message ?? "媒體操作失敗")),
        _ => StatusCode(
            StatusCodes.Status500InternalServerError,
            ApiResponse<MediaResponse>.Fail("媒體操作失敗"))
    };

    private IActionResult ToContentError(ServiceError error) => error switch
    {
        ServiceError.Validation => BadRequest(ApiResponse.Fail("無效的媒體識別碼")),
        ServiceError.Unauthorized => Unauthorized(ApiResponse.Fail("需要登入才能讀取此媒體")),
        ServiceError.Forbidden => StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail("禁止存取媒體")),
        ServiceError.NotFound => NotFound(ApiResponse.Fail("媒體不存在")),
        ServiceError.Conflict => Conflict(ApiResponse.Fail("媒體內容目前無法讀取")),
        _ => StatusCode(StatusCodes.Status500InternalServerError, ApiResponse.Fail("媒體內容讀取失敗"))
    };

    private static bool TryParseEnumName<TEnum>(string? value, out TEnum parsed)
        where TEnum : struct, Enum
    {
        var normalized = value?.Trim();
        if (normalized is not null && Enum.GetNames<TEnum>().Any(
                name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return Enum.TryParse(normalized, ignoreCase: true, out parsed);
        }

        parsed = default;
        return false;
    }
}
