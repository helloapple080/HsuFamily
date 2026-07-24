using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Auth;

namespace FamilyPortal.Application.Interfaces;

/// <summary>只在 API 邊界短暫使用的認證工作階段。</summary>
public sealed record AuthSession(
    string AccessToken,
    string RefreshToken,
    DateTime AccessTokenExpiresAt,
    DateTime RefreshTokenExpiresAt,
    UserInfo User);

/// <summary>認證與帳號管理的 Application use case。</summary>
public interface IAuthService
{
    /// <summary>驗證帳號密碼並建立新的登入工作階段。</summary>
    Task<ServiceResult<AuthSession>> LoginAsync(
        string identifier,
        string password,
        CancellationToken cancellationToken);

    /// <summary>驗證並原子輪替 Refresh Token。</summary>
    Task<ServiceResult<AuthSession>> RefreshAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken);

    /// <summary>撤銷使用者的所有 Refresh Token。</summary>
    Task<ServiceResult<bool>> LogoutAsync(
        string? userId,
        CancellationToken cancellationToken);

    /// <summary>取得可公開的目前使用者資訊。</summary>
    Task<ServiceResult<UserInfo>> GetUserAsync(
        string userId,
        CancellationToken cancellationToken);

    /// <summary>由管理員建立使用者帳號。</summary>
    Task<ServiceResult<UserInfo>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken);
}
