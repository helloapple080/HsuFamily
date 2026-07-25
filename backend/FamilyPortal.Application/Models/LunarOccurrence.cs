namespace FamilyPortal.Application.Models;

/// <summary>表示從單一農曆週期規則展開的一個日期。</summary>
public sealed class LunarOccurrence
{
    /// <summary>由來源規則與國曆日期組成的穩定識別碼。</summary>
    public required string Id { get; init; }

    /// <summary>來源週期規則的事件識別碼。</summary>
    public required string SourceRuleId { get; init; }

    /// <summary>對應的國曆 UTC 午夜日期。</summary>
    public DateTime SolarDate { get; init; }

    /// <summary>農曆年。</summary>
    public int LunarYear { get; init; }

    /// <summary>農曆月（1 至 12）。</summary>
    public int LunarMonth { get; init; }

    /// <summary>農曆日（1 至 30）。</summary>
    public int LunarDay { get; init; }

    /// <summary>是否為閏月日期。</summary>
    public bool IsLeapMonth { get; init; }
}
