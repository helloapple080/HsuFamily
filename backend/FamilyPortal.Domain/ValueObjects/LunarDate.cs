namespace FamilyPortal.Domain.ValueObjects;

/// <summary>
/// 農曆日期值物件 — 用於成員農曆生日與事件農曆日期
/// </summary>
public class LunarDate
{
    /// <summary>農曆年（數字）</summary>
    public int Year { get; set; }

    /// <summary>農曆月（1-12）</summary>
    public int Month { get; set; }

    /// <summary>農曆日（1-30）</summary>
    public int Day { get; set; }

    /// <summary>是否為閏月</summary>
    public bool IsLeapMonth { get; set; }

    /// <summary>顯示文字，如「農曆三月十五」</summary>
    public string DisplayText { get; set; } = string.Empty;
}
