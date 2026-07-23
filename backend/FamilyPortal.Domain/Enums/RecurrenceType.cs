namespace FamilyPortal.Domain.Enums;

/// <summary>
/// 事件週期類型列舉 — 支援國曆與農曆週期
/// </summary>
public enum RecurrenceType
{
    /// <summary>每年（國曆）</summary>
    Yearly = 0,

    /// <summary>每月（國曆）</summary>
    Monthly = 1,

    /// <summary>每年（農曆）</summary>
    LunarYearly = 2,

    /// <summary>每月（農曆）</summary>
    LunarMonthly = 3
}
