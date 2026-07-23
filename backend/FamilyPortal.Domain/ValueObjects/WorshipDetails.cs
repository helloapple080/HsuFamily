namespace FamilyPortal.Domain.ValueObjects;

/// <summary>
/// 拜拜/祭祀細節值物件 — 嵌套於 Worship 類型的 Event
/// </summary>
public class WorshipDetails
{
    /// <summary>祭拜對象（如「土地公」、「祖先」）</summary>
    public string? Deity { get; set; }

    /// <summary>祭品清單</summary>
    public List<string> Offerings { get; set; } = new();

    /// <summary>準備事項</summary>
    public List<string> Preparations { get; set; } = new();

    /// <summary>備註</summary>
    public string? Notes { get; set; }
}
