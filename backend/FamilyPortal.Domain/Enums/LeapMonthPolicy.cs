namespace FamilyPortal.Domain.Enums;

/// <summary>農曆閏月發生策略。</summary>
public enum LeapMonthPolicy
{
    /// <summary>僅在一般月份發生。</summary>
    RegularOnly = 0,

    /// <summary>僅在閏月發生。</summary>
    LeapOnly = 1,

    /// <summary>在一般月份與閏月都發生。</summary>
    Both = 2
}
