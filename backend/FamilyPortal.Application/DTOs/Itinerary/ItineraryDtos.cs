using System.ComponentModel.DataAnnotations;
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
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Required(ErrorMessage = "行程日期為必填")]
    public DateTime Date { get; set; }

    public DateTime? EndDate { get; set; }
    public string? Location { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<ParticipantSnapshot> Participants { get; set; } = new();
    public List<string> Photos { get; set; } = new();
    public string? Notes { get; set; }
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
    }
}

/// <summary>分頁查詢請求 DTO</summary>
public class PagedItineraryRequest
{
    /// <summary>篩選標籤</summary>
    public string? Tag { get; set; }

    /// <summary>游標日期</summary>
    public DateTime? CursorDate { get; set; }

    /// <summary>游標 ID</summary>
    public string? CursorId { get; set; }

    /// <summary>每頁筆數（預設 20，最大 50）</summary>
    [Range(1, 50)]
    public int PageSize { get; set; } = 20;
}
