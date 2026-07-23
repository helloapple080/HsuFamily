using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FamilyPortal.Domain.Entities;

/// <summary>
/// 所有實體的基底類別
/// 提供共用的 Id、建立時間、更新時間欄位
/// </summary>
public abstract class BaseEntity
{
    /// <summary>MongoDB 文件 ID</summary>
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    /// <summary>建立時間 (UTC)</summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最後更新時間 (UTC)</summary>
    [BsonElement("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
