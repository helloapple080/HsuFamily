using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Application.DTOs.Member;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Application.Services;

/// <summary>協調家庭成員資料存取與不可信輸入的正規化。</summary>
public sealed class MemberService(IMemberRepository memberRepository) : IMemberService
{
    private static readonly string[] LunarMonths =
        ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "臘"];

    private static readonly string[] LunarDays =
    [
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
    ];

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<MemberResponse>>> GetActiveAsync(
        CancellationToken cancellationToken)
    {
        var members = await memberRepository.GetActiveOrderedAsync(
            ValidationLimits.MaxPageSize,
            cancellationToken);

        return ServiceResult<IReadOnlyList<MemberResponse>>.Success(
            members.Select(MapToResponse).ToList());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<MemberResponse>> GetByIdAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var member = await memberRepository.GetByIdAsync(id, cancellationToken);
        return member is null
            ? ServiceResult<MemberResponse>.Failure(ServiceError.NotFound, "成員不存在")
            : ServiceResult<MemberResponse>.Success(MapToResponse(member));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<MemberResponse>> CreateAsync(
        UpsertMemberRequest request,
        CancellationToken cancellationToken)
    {
        var member = MapRequest(request);
        await memberRepository.CreateAsync(member, cancellationToken);
        return ServiceResult<MemberResponse>.Success(MapToResponse(member));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<MemberResponse>> UpdateAsync(
        string id,
        UpsertMemberRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await memberRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return ServiceResult<MemberResponse>.Failure(ServiceError.NotFound, "成員不存在");
        }

        var member = MapRequest(request);
        member.Id = existing.Id;
        member.CreatedAt = existing.CreatedAt;
        member.UpdatedAt = DateTime.UtcNow;

        await memberRepository.UpdateAsync(id, member, cancellationToken);
        return ServiceResult<MemberResponse>.Success(MapToResponse(member));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteAsync(
        string id,
        CancellationToken cancellationToken)
    {
        var existing = await memberRepository.GetByIdAsync(id, cancellationToken);
        if (existing is null)
        {
            return ServiceResult<bool>.Failure(ServiceError.NotFound, "成員不存在");
        }

        await memberRepository.DeleteAsync(id, cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    private static Member MapRequest(UpsertMemberRequest request) => new()
    {
        Name = request.Name,
        Title = request.Title,
        Birthday = request.Birthday,
        LunarBirthday = MapLunarDate(request.LunarBirthday),
        AvatarUrl = request.AvatarUrl,
        Bio = request.Bio,
        ContactInfo = MapContactInfo(request.ContactInfo),
        BloodType = request.BloodType,
        SortOrder = request.SortOrder,
        IsActive = request.IsActive
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

    private static ContactInfo? MapContactInfo(ContactInfoRequest? request) =>
        request is null
            ? null
            : new ContactInfo
            {
                Phone = request.Phone,
                LineId = request.LineId
            };

    private static MemberResponse MapToResponse(Member member) => new()
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

    private static string FormatLunarDate(int month, int day, bool isLeapMonth) =>
        $"農曆{(isLeapMonth ? "閏" : string.Empty)}{LunarMonths[month - 1]}月{LunarDays[day - 1]}";
}
