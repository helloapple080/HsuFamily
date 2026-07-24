using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.ValueObjects;

namespace FamilyPortal.Application.Interfaces;

/// <summary>
/// JWT Token 服務介面 — 負責生成與驗證 Access/Refresh Token
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// 生成 JWT Access Token
    /// </summary>
    /// <param name="user">使用者實體</param>
    /// <returns>JWT Token 與其精確過期時間</returns>
    GeneratedAccessToken GenerateAccessToken(User user);

    /// <summary>
    /// 生成 Refresh Token
    /// </summary>
    /// <param name="familyId">既有 token family；首次登入時省略</param>
    /// <returns>只將原始 Token 回傳給瀏覽器，資料庫僅保存雜湊</returns>
    GeneratedRefreshToken GenerateRefreshToken(string? familyId = null);

    /// <summary>將原始 Refresh Token 轉換為固定長度雜湊。</summary>
    /// <param name="token">原始 Refresh Token</param>
    /// <returns>Base64 格式 SHA-256 雜湊</returns>
    string HashRefreshToken(string token);

    /// <summary>
    /// 從過期的 Access Token 中提取使用者 ID
    /// 用於 Token 更新流程
    /// </summary>
    /// <param name="token">過期的 Access Token</param>
    /// <returns>使用者 ID，若無效則回傳 null</returns>
    string? GetUserIdFromExpiredToken(string token);
}

/// <summary>新產生的 Access Token 與其精確過期時間。</summary>
public sealed record GeneratedAccessToken(string Token, DateTime ExpiresAt);

/// <summary>新產生的 Refresh Token 與其可持久化安全版本。</summary>
public sealed class GeneratedRefreshToken
{
    /// <summary>僅寫入 HttpOnly Cookie 的原始 Token。</summary>
    public required string Token { get; init; }

    /// <summary>僅含雜湊的持久化值物件。</summary>
    public required RefreshToken PersistedToken { get; init; }
}
