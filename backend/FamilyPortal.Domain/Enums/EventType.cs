namespace FamilyPortal.Domain.Enums;

/// <summary>
/// 行事曆事件類型列舉
/// </summary>
public enum EventType
{
    /// <summary>一般事件</summary>
    General = 0,

    /// <summary>生日</summary>
    Birthday = 1,

    /// <summary>傳統節日</summary>
    Festival = 2,

    /// <summary>拜拜/祭祀</summary>
    Worship = 3,

    /// <summary>紀念日</summary>
    Anniversary = 4,

    /// <summary>重要會議</summary>
    Meeting = 5,

    /// <summary>其他</summary>
    Other = 6
}
