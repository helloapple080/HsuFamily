using System.ComponentModel.DataAnnotations;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Application.DTOs.Member;

/// <summary>家庭成員回應 DTO</summary>
public class MemberResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime Birthday { get; set; }
    public LunarDate? LunarBirthday { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public ContactInfo? ContactInfo { get; set; }
    public string? BloodType { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>建立/更新家庭成員 DTO</summary>
public class UpsertMemberRequest
{
    [Required(ErrorMessage = "姓名為必填欄位")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "稱謂為必填欄位")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "生日為必填欄位")]
    public DateTime Birthday { get; set; }

    public LunarDate? LunarBirthday { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Bio { get; set; }
    public ContactInfo? ContactInfo { get; set; }
    public string? BloodType { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
