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
    /// <returns>JWT Token 字串</returns>
    string GenerateAccessToken(User user);

    /// <summary>
    /// 生成 Refresh Token
    /// </summary>
    /// <returns>Refresh Token 值物件</returns>
    RefreshToken GenerateRefreshToken();

    /// <summary>
    /// 從過期的 Access Token 中提取使用者 ID
    /// 用於 Token 更新流程
    /// </summary>
    /// <param name="token">過期的 Access Token</param>
    /// <returns>使用者 ID，若無效則回傳 null</returns>
    string? GetUserIdFromExpiredToken(string token);
}
