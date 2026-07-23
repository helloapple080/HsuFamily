namespace FamilyPortal.Domain.ValueObjects;

/// <summary>
/// Refresh Token 值物件 — 嵌套於 User 實體
/// 支援 Token 輪替 (Rotation) 機制
/// </summary>
public class RefreshToken
{
    /// <summary>Token 值</summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>過期時間</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>建立時間</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>撤銷時間（null 表示有效）</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>Token 輪替時指向新 Token</summary>
    public string? ReplacedByToken { get; set; }

    /// <summary>是否已過期</summary>
    public bool IsExpired => DateTime.UtcNow >= ExpiresAt;

    /// <summary>是否已撤銷</summary>
    public bool IsRevoked => RevokedAt != null;

    /// <summary>是否仍然有效</summary>
    public bool IsActive => !IsRevoked && !IsExpired;
}
