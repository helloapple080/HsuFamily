using FamilyPortal.Domain.Entities;

namespace FamilyPortal.Application.Interfaces;

/// <summary>
/// 使用者 Repository 介面 — 擴充 User 專屬的查詢方法
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>根據使用者名稱取得使用者</summary>
    Task<User?> GetByUsernameAsync(string username);

    /// <summary>根據電子信箱取得使用者</summary>
    Task<User?> GetByEmailAsync(string email);

    /// <summary>根據 Refresh Token 雜湊取得所屬使用者。</summary>
    Task<User?> GetByRefreshTokenHashAsync(string tokenHash);

    /// <summary>以新的 Token family 取代既有登入工作階段。</summary>
    Task ReplaceRefreshTokensAsync(
        string userId,
        IReadOnlyCollection<Domain.ValueObjects.RefreshToken> refreshTokens);

    /// <summary>以原子操作輪替仍有效的 Refresh Token。</summary>
    Task<bool> RotateRefreshTokenAsync(
        string userId,
        string currentTokenHash,
        Domain.ValueObjects.RefreshToken newRefreshToken);

    /// <summary>撤銷指定 token family 的所有有效 Token。</summary>
    Task RevokeTokenFamilyAsync(string userId, string familyId);

    /// <summary>撤銷使用者的所有 Refresh Token。</summary>
    Task RevokeAllRefreshTokensAsync(string userId);
}
