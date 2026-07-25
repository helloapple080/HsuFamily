using FamilyPortal.Domain.Enums;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FamilyPortal.Domain.ValueObjects;

/// <summary>
/// 週期性規則值物件 — 嵌套於 Event 實體中
/// 支援國曆與農曆兩種週期
/// </summary>
public class RecurrenceRule
{
    /// <summary>週期類型</summary>
    [BsonRepresentation(BsonType.String)]
    public RecurrenceType Type { get; set; }

    /// <summary>間隔數（例如每 1 年、每 2 月）</summary>
    public int Interval { get; set; } = 1;

    /// <summary>
    /// 農曆固定日清單，用於 LunarMonthly 類型
    /// 例如 [1, 15] 代表初一十五
    /// </summary>
    public List<int>? LunarDays { get; set; }

    /// <summary>農曆閏月發生策略。</summary>
    [BsonRepresentation(BsonType.String)]
    public LeapMonthPolicy LeapMonthPolicy { get; set; } = LeapMonthPolicy.RegularOnly;

    /// <summary>週期規則生效的 UTC 日期時間。</summary>
    public DateTime? EffectiveFrom { get; set; }

    /// <summary>週期規則結束的 UTC 日期時間。</summary>
    public DateTime? EffectiveUntil { get; set; }
}
