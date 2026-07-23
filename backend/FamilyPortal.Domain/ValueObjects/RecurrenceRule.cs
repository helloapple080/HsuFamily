using FamilyPortal.Domain.Enums;

namespace FamilyPortal.Domain.ValueObjects;

/// <summary>
/// 週期性規則值物件 — 嵌套於 Event 實體中
/// 支援國曆與農曆兩種週期
/// </summary>
public class RecurrenceRule
{
    /// <summary>週期類型</summary>
    public RecurrenceType Type { get; set; }

    /// <summary>間隔數（例如每 1 年、每 2 月）</summary>
    public int Interval { get; set; } = 1;

    /// <summary>
    /// 農曆固定日清單，用於 LunarMonthly 類型
    /// 例如 [1, 15] 代表初一十五
    /// </summary>
    public List<int>? LunarDays { get; set; }
}
