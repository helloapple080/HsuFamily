using FamilyPortal.Domain.Entities;

namespace FamilyPortal.Application.Interfaces;

/// <summary>
/// 使用者 Repository 介面 — 擴充 User 專屬的查詢方法
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>根據使用者名稱取得使用者</summary>
    Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>根據電子信箱取得使用者</summary>
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>根據 Refresh Token 雜湊取得所屬使用者。</summary>
    Task<User?> GetByRefreshTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default);

    /// <summary>原子附加新的登入工作階段並限制歷史筆數。</summary>
    Task AddRefreshTokenAsync(
        string userId,
        Domain.ValueObjects.RefreshToken refreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>以原子操作輪替仍有效的 Refresh Token。</summary>
    Task<bool> RotateRefreshTokenAsync(
        string userId,
        string currentTokenHash,
        Domain.ValueObjects.RefreshToken newRefreshToken,
        CancellationToken cancellationToken = default);

    /// <summary>撤銷指定 token family 的所有有效 Token。</summary>
    Task RevokeTokenFamilyAsync(
        string userId,
        string familyId,
        CancellationToken cancellationToken = default);

    /// <summary>撤銷使用者的所有 Refresh Token。</summary>
    Task RevokeAllRefreshTokensAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
