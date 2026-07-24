using System.ComponentModel.DataAnnotations;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Application.DTOs.Event;

/// <summary>行事曆事件回應 DTO</summary>
public class EventResponse
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string EventType { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsLunarDate { get; set; }
    public LunarDate? LunarDate { get; set; }
    public RecurrenceRule? RecurrenceRule { get; set; }
    public WorshipDetails? WorshipDetails { get; set; }
    public List<string> RelatedMemberIds { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public string? Color { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>建立/更新事件 DTO</summary>
public class UpsertEventRequest : IValidatableObject
{
    [Required(ErrorMessage = "事件標題為必填")]
    [MaxLength(ValidationLimits.MaxTitleLength, ErrorMessage = "標題最多 200 字元")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(ValidationLimits.MaxDescriptionLength, ErrorMessage = "描述最多 4000 個字元")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "事件類型為必填")]
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "事件類型最多 200 個字元")]
    public string EventType { get; set; } = "General";

    [Required(ErrorMessage = "開始日期為必填")]
    public DateTime StartDate { get; set; }

    public DateTime? EndDate { get; set; }
    public bool IsAllDay { get; set; }
    public bool IsLunarDate { get; set; }
    public LunarDateRequest? LunarDate { get; set; }
    public RecurrenceRuleRequest? RecurrenceRule { get; set; }
    public WorshipDetailsRequest? WorshipDetails { get; set; }
    public List<string> RelatedMemberIds { get; set; } = new();
    public List<string> Tags { get; set; } = new();

    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "顏色最多 200 個字元")]
    public string? Color { get; set; }

    /// <summary>跨欄位驗證事件資料一致性</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate.HasValue && EndDate.Value < StartDate)
        {
            yield return new ValidationResult(
                "結束日期不可早於開始日期",
                new[] { nameof(EndDate) });
        }

        if (IsLunarDate && LunarDate is null)
        {
            yield return new ValidationResult(
                "農曆事件必須提供農曆日期",
                new[] { nameof(LunarDate) });
        }

        if (!IsLunarDate && LunarDate is not null)
        {
            yield return new ValidationResult(
                "非農曆事件不可提供農曆日期",
                new[] { nameof(LunarDate) });
        }

        if (!string.Equals(EventType, "Worship", StringComparison.OrdinalIgnoreCase) &&
            WorshipDetails is not null)
        {
            yield return new ValidationResult(
                "只有拜拜事件可以提供祭祀細節",
                new[] { nameof(WorshipDetails) });
        }

        if (LunarDate is not null)
        {
            foreach (var result in RequestValidation.ValidateNested(LunarDate, nameof(LunarDate)))
            {
                yield return result;
            }
        }

        if (RecurrenceRule is not null)
        {
            foreach (var result in RequestValidation.ValidateNested(RecurrenceRule, nameof(RecurrenceRule)))
            {
                yield return result;
            }
        }

        if (WorshipDetails is not null)
        {
            foreach (var result in RequestValidation.ValidateNested(WorshipDetails, nameof(WorshipDetails)))
            {
                yield return result;
            }
        }

        foreach (var result in RequestValidation.ValidateStringCollection(
                     RelatedMemberIds,
                     nameof(RelatedMemberIds),
                     ValidationLimits.MaxRelatedMembers,
                     ValidationLimits.MaxShortTextLength))
        {
            yield return result;
        }

        foreach (var result in RequestValidation.ValidateStringCollection(
                     Tags,
                     nameof(Tags),
                     ValidationLimits.MaxTags,
                     ValidationLimits.MaxTagLength))
        {
            yield return result;
        }
    }
}

/// <summary>事件日期範圍查詢 DTO。</summary>
public sealed class EventRangeRequest : IValidatableObject
{
    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    /// <summary>驗證日期先後與查詢範圍上限。</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (End < Start)
        {
            yield return new ValidationResult("結束日期不可早於開始日期", [nameof(End)]);
            yield break;
        }

        if (End - Start > TimeSpan.FromDays(ValidationLimits.MaxDateRangeDays))
        {
            yield return new ValidationResult(
                $"日期範圍不可超過 {ValidationLimits.MaxDateRangeDays} 天",
                [nameof(End)]);
        }
    }
}
