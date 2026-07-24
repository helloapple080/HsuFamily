using System.ComponentModel.DataAnnotations;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Common;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Application.DTOs.Itinerary;

/// <summary>行程紀錄回應 DTO</summary>
public class ItineraryResponse
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime Date { get; set; }
    public DateTime? EndDate { get; set; }
    public string? Location { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<ParticipantSnapshot> Participants { get; set; } = new();
    public List<string> Photos { get; set; } = new();
    public string? Notes { get; set; }
    public string? LinkedEventId { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>建立/更新行程 DTO</summary>
public class UpsertItineraryRequest : IValidatableObject
{
    [Required(ErrorMessage = "行程標題為必填")]
    [MaxLength(ValidationLimits.MaxTitleLength, ErrorMessage = "標題最多 200 個字元")]
    public string Title { get; set; } = string.Empty;

    [MaxLength(ValidationLimits.MaxDescriptionLength, ErrorMessage = "描述最多 4000 個字元")]
    public string? Description { get; set; }

    [Required(ErrorMessage = "行程日期為必填")]
    public DateTime Date { get; set; }

    public DateTime? EndDate { get; set; }

    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "地點最多 200 個字元")]
    public string? Location { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<ParticipantRequest> Participants { get; set; } = new();
    public List<string> Photos { get; set; } = new();

    [MaxLength(ValidationLimits.MaxNotesLength, ErrorMessage = "備註最多 8000 個字元")]
    public string? Notes { get; set; }

    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "關聯事件 ID 最多 200 個字元")]
    public string? LinkedEventId { get; set; }

    /// <summary>跨欄位驗證行程資料一致性</summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (EndDate.HasValue && EndDate.Value < Date)
        {
            yield return new ValidationResult(
                "結束日期不可早於行程日期",
                new[] { nameof(EndDate) });
        }

        foreach (var result in RequestValidation.ValidateStringCollection(
                     Tags,
                     nameof(Tags),
                     ValidationLimits.MaxTags,
                     ValidationLimits.MaxTagLength))
        {
            yield return result;
        }

        if (Participants is null)
        {
            yield return new ValidationResult("參與者不可為 null", [nameof(Participants)]);
        }
        else
        {
            if (Participants.Count > ValidationLimits.MaxParticipants)
            {
                yield return new ValidationResult(
                    $"參與者最多 {ValidationLimits.MaxParticipants} 筆",
                    [nameof(Participants)]);
            }

            foreach (var participant in Participants)
            {
                if (participant is null)
                {
                    yield return new ValidationResult("參與者不可包含 null", [nameof(Participants)]);
                    continue;
                }

                foreach (var result in RequestValidation.ValidateNested(participant, nameof(Participants)))
                {
                    yield return result;
                }
            }
        }

        foreach (var result in RequestValidation.ValidateStringCollection(
                     Photos,
                     nameof(Photos),
                     ValidationLimits.MaxPhotos,
                     ValidationLimits.MaxUrlLength,
                     requireHttpUrl: true))
        {
            yield return result;
        }
    }
}

/// <summary>分頁查詢請求 DTO</summary>
public class PagedItineraryRequest
{
    /// <summary>篩選標籤</summary>
    [MaxLength(ValidationLimits.MaxTagLength, ErrorMessage = "標籤最多 50 個字元")]
    public string? Tag { get; set; }

    /// <summary>游標日期</summary>
    public DateTime? CursorDate { get; set; }

    /// <summary>游標 ID</summary>
    [MaxLength(ValidationLimits.MaxShortTextLength, ErrorMessage = "游標 ID 最多 200 個字元")]
    public string? CursorId { get; set; }

    /// <summary>每頁筆數（預設 20，最大 100）</summary>
    [Range(1, ValidationLimits.MaxPageSize)]
    public int PageSize { get; set; } = 20;
}
