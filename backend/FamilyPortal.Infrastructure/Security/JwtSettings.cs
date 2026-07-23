namespace FamilyPortal.Infrastructure.Security;

/// <summary>
/// JWT 組態設定 — 對應 appsettings.json 中的 JwtSettings 區塊
/// </summary>
public class JwtSettings
{
    /// <summary>簽名金鑰（至少 32 字元）</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Token 發行者</summary>
    public string Issuer { get; set; } = "FamilyPortal";

    /// <summary>Token 接收者</summary>
    public string Audience { get; set; } = "FamilyPortalApp";

    /// <summary>Access Token 有效時間（分鐘）</summary>
    public int AccessTokenExpirationMinutes { get; set; } = 30;

    /// <summary>Refresh Token 有效時間（天）</summary>
    public int RefreshTokenExpirationDays { get; set; } = 7;
}
