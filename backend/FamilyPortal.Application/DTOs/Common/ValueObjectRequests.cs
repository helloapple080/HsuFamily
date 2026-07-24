using System.ComponentModel.DataAnnotations;
using FamilyPortal.Application.Common;
using FamilyPortal.Domain.Enums;

namespace FamilyPortal.Application.DTOs.Common;

/// <summary>農曆日期 transport request。</summary>
public sealed class LunarDateRequest
{
    [Range(1900, 2200, ErrorMessage = "農曆年份必須介於 1900 至 2200")]
    public int Year { get; set; }

    [Range(1, 12, ErrorMessage = "農曆月份必須介於 1 至 12")]
    public int Month { get; set; }

    [Range(1, 30, ErrorMessage = "農曆日期必須介於 1 至 30")]
    public int Day { get; set; }

    public bool IsLeapMonth { get; set; }

    [MaxLength(32, ErrorMessage = "農曆顯示文字最多 32 個字元")]
    public string? DisplayText { get; set; }
}

/// <summary>週期規則 transport request。</summary>
public sealed class RecurrenceRuleRequest : IValidatableObject
{
    [EnumDataType(typeof(RecurrenceType), ErrorMessage = "週期類型無效")]
    public RecurrenceType Type { get; set; }

    [Range(1, ValidationLimits.MaxRecurrenceInterval, ErrorMessage = "週期間隔必須介於 1 至 120")]
    public int Interval { get; set; } = 1;

    public List<int>? LunarDays { get; set; }

    /// <summary>驗證農曆日期清單的數量與每個值。</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Interval is < 1 or > ValidationLimits.MaxRecurrenceInterval)
        {
            yield return new ValidationResult(
                $"週期間隔必須介於 1 至 {ValidationLimits.MaxRecurrenceInterval}",
                [nameof(Interval)]);
        }

        if (LunarDays is null)
        {
            yield break;
        }

        if (LunarDays.Count > 30)
        {
            yield return new ValidationResult("農曆日期清單最多 30 筆", [nameof(LunarDays)]);
        }

        if (LunarDays.Any(day => day is < 1 or > 30))
        {
            yield return new ValidationResult("農曆日期必須介於 1 至 30", [nameof(LunarDays)]);
        }
    }
}

/// <summary>祭祀細節 transport request。</summary>
public sealed class WorshipDetailsRequest : IValidatableObject
{
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "祭拜對象最多 200 個字元")]
    public string? Deity { get; set; }

    public List<string> Offerings { get; set; } = new();

    public List<string> Preparations { get; set; } = new();

    [MaxLength(ValidationLimits.MaxNotesLength, ErrorMessage = "備註最多 8000 個字元")]
    public string? Notes { get; set; }

    /// <summary>驗證祭品與準備事項的數量、null 與每項長度。</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in RequestValidation.ValidateStringCollection(
                     Offerings,
                     nameof(Offerings),
                     ValidationLimits.MaxTags,
                     ValidationLimits.MaxShortTextLength))
        {
            yield return result;
        }

        foreach (var result in RequestValidation.ValidateStringCollection(
                     Preparations,
                     nameof(Preparations),
                     ValidationLimits.MaxTags,
                     ValidationLimits.MaxShortTextLength))
        {
            yield return result;
        }
    }
}

/// <summary>聯絡資訊 transport request。</summary>
public sealed class ContactInfoRequest
{
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "電話號碼最多 200 個字元")]
    public string? Phone { get; set; }

    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "LINE ID 最多 200 個字元")]
    public string? LineId { get; set; }
}

/// <summary>行程參與者 transport request。</summary>
public sealed class ParticipantRequest
{
    [Required(ErrorMessage = "成員 ID 為必填欄位")]
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "成員 ID 最多 200 個字元")]
    public string MemberId { get; set; } = string.Empty;

    [Required(ErrorMessage = "成員姓名為必填欄位")]
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "成員姓名最多 200 個字元")]
    public string Name { get; set; } = string.Empty;
}

internal static class RequestValidation
{
    internal static IEnumerable<ValidationResult> ValidateNested(object value, string memberName)
    {
        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(value, new ValidationContext(value), results, validateAllProperties: true))
        {
            yield break;
        }

        foreach (var result in results)
        {
            yield return new ValidationResult(result.ErrorMessage, [memberName]);
        }
    }

    internal static IEnumerable<ValidationResult> ValidateStringCollection(
        IReadOnlyCollection<string>? values,
        string memberName,
        int maximumCount,
        int maximumItemLength,
        bool requireHttpUrl = false)
    {
        if (values is null)
        {
            yield return new ValidationResult($"{memberName} 不可為 null", [memberName]);
            yield break;
        }

        if (values.Count > maximumCount)
        {
            yield return new ValidationResult($"{memberName} 最多 {maximumCount} 筆", [memberName]);
        }

        foreach (var value in values)
        {
            if (value is null)
            {
                yield return new ValidationResult($"{memberName} 不可包含 null", [memberName]);
                continue;
            }

            if (value.Length > maximumItemLength)
            {
                yield return new ValidationResult($"{memberName} 每筆最多 {maximumItemLength} 個字元", [memberName]);
            }

            if (requireHttpUrl && !IsHttpUrl(value))
            {
                yield return new ValidationResult($"{memberName} 必須是有效的 HTTP(S) URL", [memberName]);
            }
        }
    }

    internal static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
