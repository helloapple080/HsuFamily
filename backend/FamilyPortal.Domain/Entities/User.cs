using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Domain.Entities;

/// <summary>
/// 使用者實體 — 對應 MongoDB Users Collection
/// 負責系統登入認證與權限控制
/// </summary>
[BsonIgnoreExtraElements]
public class User : BaseEntity
{
    /// <summary>登入帳號（唯一）</summary>
    [BsonElement("username")]
    public string Username { get; set; } = string.Empty;

    /// <summary>電子信箱（唯一）</summary>
    [BsonElement("email")]
    public string Email { get; set; } = string.Empty;

    /// <summary>Argon2id 加密後的密碼雜湊</summary>
    [BsonElement("passwordHash")]
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>使用者角色 (Admin / User)</summary>
    [BsonElement("role")]
    [BsonRepresentation(BsonType.String)]
    public UserRole Role { get; set; } = UserRole.User;

    /// <summary>顯示名稱</summary>
    [BsonElement("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>關聯的家庭成員 ID（可選）</summary>
    [BsonElement("linkedMemberId")]
    [BsonRepresentation(BsonType.ObjectId)]
    public string? LinkedMemberId { get; set; }

    /// <summary>Refresh Token 輪替歷史（僅保存雜湊）</summary>
    [BsonElement("refreshTokens")]
    public List<RefreshToken> RefreshTokens { get; set; } = [];

    /// <summary>帳號是否啟用</summary>
    [BsonElement("isActive")]
    public bool IsActive { get; set; } = true;
}
