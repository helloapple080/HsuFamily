using System.ComponentModel.DataAnnotations;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Common;
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
public class UpsertMemberRequest : IValidatableObject
{
    [Required(ErrorMessage = "姓名為必填欄位")]
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "姓名最多 200 個字元")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "稱謂為必填欄位")]
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "稱謂最多 200 個字元")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "生日為必填欄位")]
    public DateTime Birthday { get; set; }

    public LunarDateRequest? LunarBirthday { get; set; }

    [Url(ErrorMessage = "大頭貼 URL 格式不正確")]
    [MaxLength(ValidationLimits.MaxUrlLength, ErrorMessage = "大頭貼 URL 最多 2048 個字元")]
    public string? AvatarUrl { get; set; }

    [MaxLength(ValidationLimits.MaxDescriptionLength, ErrorMessage = "簡介最多 4000 個字元")]
    public string? Bio { get; set; }
    public ContactInfoRequest? ContactInfo { get; set; }

    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "血型最多 200 個字元")]
    public string? BloodType { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>明確遞迴驗證農曆生日與聯絡資訊。</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AvatarUrl is not null && !RequestValidation.IsHttpUrl(AvatarUrl))
        {
            yield return new ValidationResult(
                "大頭貼必須是有效的 HTTP(S) URL",
                [nameof(AvatarUrl)]);
        }

        if (LunarBirthday is not null)
        {
            foreach (var result in RequestValidation.ValidateNested(LunarBirthday, nameof(LunarBirthday)))
            {
                yield return result;
            }
        }

        if (ContactInfo is not null)
        {
            foreach (var result in RequestValidation.ValidateNested(ContactInfo, nameof(ContactInfo)))
            {
                yield return result;
            }
        }
    }
}
