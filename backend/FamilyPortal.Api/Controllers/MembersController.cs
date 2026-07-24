using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Member;
using FamilyPortal.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyPortal.Api.Controllers;

/// <summary>家庭成員 HTTP adapter。</summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MembersController(IMemberService memberService) : ControllerBase
{
    /// <summary>GET /api/members — 取得數量受限且依排序順序排列的啟用家庭成員。</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<MemberResponse>>>> GetAll(
        CancellationToken cancellationToken)
    {
        var result = await memberService.GetActiveAsync(cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<List<MemberResponse>>.Ok([.. result.Value!]))
            : ToListActionResult(result);
    }

    /// <summary>GET /api/members/{id} — 取得單一成員。</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<MemberResponse>>> GetById(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await memberService.GetByIdAsync(id, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<MemberResponse>.Ok(result.Value!))
            : ToActionResult(result);
    }

    /// <summary>POST /api/members — 新增家庭成員（僅 Admin）。</summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<MemberResponse>>> Create(
        [FromBody] UpsertMemberRequest request,
        CancellationToken cancellationToken)
    {
        var result = await memberService.CreateAsync(request, cancellationToken);
        return result.IsSuccess
            ? CreatedAtAction(
                nameof(GetById),
                new { id = result.Value!.Id },
                ApiResponse<MemberResponse>.Ok(result.Value, "成員新增成功"))
            : ToActionResult(result);
    }

    /// <summary>PUT /api/members/{id} — 更新家庭成員（僅 Admin）。</summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<MemberResponse>>> Update(
        string id,
        [FromBody] UpsertMemberRequest request,
        CancellationToken cancellationToken)
    {
        var result = await memberService.UpdateAsync(id, request, cancellationToken);
        return result.IsSuccess
            ? Ok(ApiResponse<MemberResponse>.Ok(result.Value!, "成員更新成功"))
            : ToActionResult(result);
    }

    /// <summary>DELETE /api/members/{id} — 刪除家庭成員（僅 Admin）。</summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse>> Delete(
        string id,
        CancellationToken cancellationToken)
    {
        var result = await memberService.DeleteAsync(id, cancellationToken);
        if (result.IsSuccess)
        {
            return Ok(ApiResponse.Ok("成員刪除成功"));
        }

        return result.Error switch
        {
            ServiceError.Validation => BadRequest(ApiResponse.Fail(result.Message ?? "驗證失敗")),
            ServiceError.Unauthorized => Unauthorized(ApiResponse.Fail(result.Message ?? "未授權")),
            ServiceError.Forbidden => StatusCode(
                StatusCodes.Status403Forbidden,
                ApiResponse.Fail(result.Message ?? "禁止存取")),
            ServiceError.NotFound => NotFound(ApiResponse.Fail(result.Message ?? "成員不存在")),
            ServiceError.Conflict => Conflict(ApiResponse.Fail(result.Message ?? "資料衝突")),
            _ => StatusCode(
                StatusCodes.Status500InternalServerError,
                ApiResponse.Fail("成員操作失敗"))
        };
    }

    private ActionResult<ApiResponse<MemberResponse>> ToActionResult(
        ServiceResult<MemberResponse> result) => result.Error switch
    {
        ServiceError.Validation => BadRequest(ApiResponse<MemberResponse>.Fail(result.Message ?? "驗證失敗")),
        ServiceError.Unauthorized => Unauthorized(ApiResponse<MemberResponse>.Fail(result.Message ?? "未授權")),
        ServiceError.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ApiResponse<MemberResponse>.Fail(result.Message ?? "禁止存取")),
        ServiceError.NotFound => NotFound(ApiResponse<MemberResponse>.Fail(result.Message ?? "成員不存在")),
        ServiceError.Conflict => Conflict(ApiResponse<MemberResponse>.Fail(result.Message ?? "資料衝突")),
        _ => StatusCode(
            StatusCodes.Status500InternalServerError,
            ApiResponse<MemberResponse>.Fail("成員操作失敗"))
    };

    private ActionResult<ApiResponse<List<MemberResponse>>> ToListActionResult(
        ServiceResult<IReadOnlyList<MemberResponse>> result) => result.Error switch
    {
        ServiceError.Validation => BadRequest(ApiResponse<List<MemberResponse>>.Fail(result.Message ?? "驗證失敗")),
        ServiceError.Unauthorized => Unauthorized(ApiResponse<List<MemberResponse>>.Fail(result.Message ?? "未授權")),
        ServiceError.Forbidden => StatusCode(
            StatusCodes.Status403Forbidden,
            ApiResponse<List<MemberResponse>>.Fail(result.Message ?? "禁止存取")),
        ServiceError.NotFound => NotFound(ApiResponse<List<MemberResponse>>.Fail(result.Message ?? "成員不存在")),
        ServiceError.Conflict => Conflict(ApiResponse<List<MemberResponse>>.Fail(result.Message ?? "資料衝突")),
        _ => StatusCode(
            StatusCodes.Status500InternalServerError,
            ApiResponse<List<MemberResponse>>.Fail("成員操作失敗"))
    };
}
