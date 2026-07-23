using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Domain.Entities;

/// <summary>
/// 行事曆事件實體 — 對應 MongoDB Events Collection
/// 支援農曆日期、週期性規則與拜拜/祭祀細節
/// </summary>
[BsonIgnoreExtraElements]
public class Event : BaseEntity
{
    /// <summary>事件標題</summary>
    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>事件描述</summary>
    [BsonElement("description")]
    public string? Description { get; set; }

    /// <summary>事件類型</summary>
    [BsonElement("eventType")]
    [BsonRepresentation(BsonType.String)]
    public EventType EventType { get; set; } = EventType.General;

    /// <summary>開始日期時間</summary>
    [BsonElement("startDate")]
    public DateTime StartDate { get; set; }

    /// <summary>結束日期時間</summary>
    [BsonElement("endDate")]
    public DateTime? EndDate { get; set; }

    /// <summary>是否為全天事件</summary>
    [BsonElement("isAllDay")]
    public bool IsAllDay { get; set; }

    /// <summary>是否以農曆日期為基準</summary>
    [BsonElement("isLunarDate")]
    public bool IsLunarDate { get; set; }

    /// <summary>農曆日期資訊（嵌套值物件）</summary>
    [BsonElement("lunarDate")]
    public LunarDate? LunarDate { get; set; }

    /// <summary>週期性規則（嵌套值物件）</summary>
    [BsonElement("recurrenceRule")]
    public RecurrenceRule? RecurrenceRule { get; set; }

    /// <summary>拜拜/祭祀細節（嵌套值物件）</summary>
    [BsonElement("worshipDetails")]
    public WorshipDetails? WorshipDetails { get; set; }

    /// <summary>相關家庭成員 IDs (Reference)</summary>
    [BsonElement("relatedMemberIds")]
    public List<string> RelatedMemberIds { get; set; } = new();

    /// <summary>標籤</summary>
    [BsonElement("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>日曆顯示顏色 (HEX)</summary>
    [BsonElement("color")]
    public string? Color { get; set; }

    /// <summary>建立者 User ID</summary>
    [BsonElement("createdBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string CreatedBy { get; set; } = string.Empty;
}
