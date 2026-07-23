using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Domain.Entities;

/// <summary>
/// 家庭成員實體 — 對應 MongoDB Members Collection
/// 與 Users Collection 分離，不是所有成員都需要登入帳號
/// </summary>
[BsonIgnoreExtraElements]
public class Member : BaseEntity
{
    /// <summary>姓名</summary>
    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>稱謂（爸爸、媽媽、大哥等）</summary>
    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>國曆生日</summary>
    [BsonElement("birthday")]
    public DateTime Birthday { get; set; }

    /// <summary>農曆生日（嵌套值物件）</summary>
    [BsonElement("lunarBirthday")]
    public LunarDate? LunarBirthday { get; set; }

    /// <summary>大頭貼 URL</summary>
    [BsonElement("avatarUrl")]
    public string? AvatarUrl { get; set; }

    /// <summary>簡介/自我介紹</summary>
    [BsonElement("bio")]
    public string? Bio { get; set; }

    /// <summary>聯絡資訊（嵌套值物件）</summary>
    [BsonElement("contactInfo")]
    public ContactInfo? ContactInfo { get; set; }

    /// <summary>血型</summary>
    [BsonElement("bloodType")]
    public string? BloodType { get; set; }

    /// <summary>排序順序（前端顯示用）</summary>
    [BsonElement("sortOrder")]
    public int SortOrder { get; set; }

    /// <summary>是否顯示</summary>
    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;
}
