using MongoDB.Bson.Serialization.Attributes;

namespace FamilyPortal.Domain.ValueObjects;

/// <summary>
/// Refresh Token 值物件 — 嵌套於 User 實體
/// 支援 Token 輪替 (Rotation) 機制
/// </summary>
public class RefreshToken
{
    /// <summary>Token 的 SHA-256 雜湊；永不保存原始 Token</summary>
    [BsonElement("tokenHash")]
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Token family 識別碼，用於偵測輪替後的 Token 重放</summary>
    [BsonElement("familyId")]
    public string FamilyId { get; set; } = string.Empty;

    /// <summary>過期時間</summary>
    [BsonElement("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    /// <summary>建立時間</summary>
    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; }

    /// <summary>撤銷時間（null 表示有效）</summary>
    [BsonElement("revokedAt")]
    public DateTime? RevokedAt { get; set; }

    /// <summary>Token 輪替時指向新 Token 的雜湊</summary>
    [BsonElement("replacedByTokenHash")]
    public string? ReplacedByTokenHash { get; set; }

    /// <summary>是否已過期</summary>
    [BsonIgnore]
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>是否已撤銷</summary>
    [BsonIgnore]
    public bool IsRevoked => RevokedAt != null;

    /// <summary>是否仍然有效</summary>
    [BsonIgnore]
    public bool IsActive => !IsRevoked && !IsExpired;
}
