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

    /// <summary>根據 Refresh Token 取得使用者</summary>
    Task<User?> GetByRefreshTokenAsync(string token);

    /// <summary>更新使用者的 Refresh Token</summary>
    Task UpdateRefreshTokenAsync(string userId, Domain.ValueObjects.RefreshToken? refreshToken);
}
