using FamilyPortal.Application.DTOs;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Application.DTOs.Member;
using FamilyPortal.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FamilyPortal.Api.Controllers;

/// <summary>
/// 家庭成員控制器 — 成員資料的 CRUD 操作
/// Read: 所有已登入使用者
/// Create/Update/Delete: 僅 Admin
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MembersController : ControllerBase
{
    private readonly IMemberRepository _memberRepo;
    private readonly ILogger<MembersController> _logger;

    public MembersController(
        IMemberRepository memberRepo,
        ILogger<MembersController> logger)
    {
        _memberRepo = memberRepo;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/members — 取得所有啟用的家庭成員（依排序順序）
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<MemberResponse>>>> GetAll()
    {
        var members = await _memberRepo.GetActiveOrderedAsync();
        var response = members.Select(MapToResponse).ToList();
        return Ok(ApiResponse<List<MemberResponse>>.Ok(response));
    }

    /// <summary>
    /// GET /api/members/{id} — 取得單一成員
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<MemberResponse>>> GetById(string id)
    {
        var member = await _memberRepo.GetByIdAsync(id);
        if (member == null)
        {
            return NotFound(ApiResponse<MemberResponse>.Fail("成員不存在"));
        }

        return Ok(ApiResponse<MemberResponse>.Ok(MapToResponse(member)));
    }

    /// <summary>
    /// POST /api/members — 新增家庭成員（僅 Admin）
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<MemberResponse>>> Create([FromBody] UpsertMemberRequest request)
    {
        var member = new Domain.Entities.Member
        {
            Name = request.Name,
            Title = request.Title,
            Birthday = request.Birthday,
            LunarBirthday = MapLunarDateRequest(request.LunarBirthday),
            AvatarUrl = request.AvatarUrl,
            Bio = request.Bio,
            ContactInfo = MapContactInfoRequest(request.ContactInfo),
            BloodType = request.BloodType,
            SortOrder = request.SortOrder,
            IsActive = request.IsActive
        };

        await _memberRepo.CreateAsync(member);
        _logger.LogInformation("新增家庭成員：{MemberId}", member.Id);

        return CreatedAtAction(
            nameof(GetById),
            new { id = member.Id },
            ApiResponse<MemberResponse>.Ok(MapToResponse(member), "成員新增成功"));
    }

    /// <summary>
    /// PUT /api/members/{id} — 更新家庭成員（僅 Admin）
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse<MemberResponse>>> Update(
        string id, [FromBody] UpsertMemberRequest request)
    {
        var member = await _memberRepo.GetByIdAsync(id);
        if (member == null)
        {
            return NotFound(ApiResponse<MemberResponse>.Fail("成員不存在"));
        }

        member.Name = request.Name;
        member.Title = request.Title;
        member.Birthday = request.Birthday;
        member.LunarBirthday = MapLunarDateRequest(request.LunarBirthday);
        member.AvatarUrl = request.AvatarUrl;
        member.Bio = request.Bio;
        member.ContactInfo = MapContactInfoRequest(request.ContactInfo);
        member.BloodType = request.BloodType;
        member.SortOrder = request.SortOrder;
        member.IsActive = request.IsActive;

        await _memberRepo.UpdateAsync(id, member);
        _logger.LogInformation("更新家庭成員：{MemberId}", member.Id);

        return Ok(ApiResponse<MemberResponse>.Ok(MapToResponse(member), "成員更新成功"));
    }

    /// <summary>
    /// DELETE /api/members/{id} — 刪除家庭成員（僅 Admin）
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<ApiResponse>> Delete(string id)
    {
        var member = await _memberRepo.GetByIdAsync(id);
        if (member == null)
        {
            return NotFound(ApiResponse.Fail("成員不存在"));
        }

        await _memberRepo.DeleteAsync(id);
        _logger.LogInformation("刪除家庭成員：{MemberId}", member.Id);

        return Ok(ApiResponse.Ok("成員刪除成功"));
    }

    /// <summary>Entity 轉 DTO 映射</summary>
    private static MemberResponse MapToResponse(Domain.Entities.Member member) => new()
    {
        Id = member.Id,
        Name = member.Name,
        Title = member.Title,
        Birthday = member.Birthday,
        LunarBirthday = member.LunarBirthday,
        AvatarUrl = member.AvatarUrl,
        Bio = member.Bio,
        ContactInfo = member.ContactInfo,
        BloodType = member.BloodType,
        SortOrder = member.SortOrder,
        IsActive = member.IsActive
    };

    private static Domain.ValueObjects.LunarDate? MapLunarDateRequest(LunarDateRequest? request) =>
        request is null
            ? null
            : new Domain.ValueObjects.LunarDate
            {
                Year = request.Year,
                Month = request.Month,
                Day = request.Day,
                IsLeapMonth = request.IsLeapMonth,
                DisplayText = request.DisplayText ?? string.Empty
            };

    private static Domain.ValueObjects.ContactInfo? MapContactInfoRequest(ContactInfoRequest? request) =>
        request is null
            ? null
            : new Domain.ValueObjects.ContactInfo
            {
                Phone = request.Phone,
                LineId = request.LineId
            };
}
