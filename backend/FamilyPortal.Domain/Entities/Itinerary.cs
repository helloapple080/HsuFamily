using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Domain.Entities;

/// <summary>
/// 歷年行程紀錄實體 — 對應 MongoDB Itineraries Collection
/// 提供歷史查詢功能，支援標籤化與分頁
/// </summary>
[BsonIgnoreExtraElements]
public class Itinerary : BaseEntity
{
    /// <summary>行程標題</summary>
    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>行程描述</summary>
    [BsonElement("description")]
    public string? Description { get; set; }

    /// <summary>行程日期</summary>
    [BsonElement("date")]
    public DateTime Date { get; set; }

    /// <summary>結束日期（多日行程）</summary>
    [BsonElement("endDate")]
    public DateTime? EndDate { get; set; }

    /// <summary>地點</summary>
    [BsonElement("location")]
    public string? Location { get; set; }

    /// <summary>標籤（家族旅遊、聚餐、重要會議、祭祀等）</summary>
    [BsonElement("tags")]
    public List<string> Tags { get; set; } = new();

    /// <summary>參與成員（反正規化快照）</summary>
    [BsonElement("participants")]
    public List<ParticipantSnapshot> Participants { get; set; } = new();

    /// <summary>照片 URL 陣列</summary>
    [BsonElement("photos")]
    public List<string> Photos { get; set; } = new();

    /// <summary>備註</summary>
    [BsonElement("notes")]
    public string? Notes { get; set; }

    /// <summary>關聯的行事曆事件 ID（可選）</summary>
    [BsonElement("linkedEventId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? LinkedEventId { get; set; }

    /// <summary>建立者 User ID</summary>
    [BsonElement("createdBy")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string CreatedBy { get; set; } = string.Empty;
}
