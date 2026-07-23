namespace FamilyPortal.Domain.ValueObjects;

/// <summary>
/// 行程參與者快照 — 反正規化儲存於 Itinerary 中
/// 避免每次查詢都需要 JOIN Members Collection
/// </summary>
public class ParticipantSnapshot
{
    /// <summary>成員 ID (Reference)</summary>
    public string MemberId { get; set; } = string.Empty;

    /// <summary>成員姓名快照</summary>
    public string Name { get; set; } = string.Empty;
}
