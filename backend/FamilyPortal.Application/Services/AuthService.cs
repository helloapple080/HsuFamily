using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Auth;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;

namespace FamilyPortal.Application.Services;

/// <summary>實作認證、Token 輪替與帳號管理 use case。</summary>
public sealed class AuthService(
    IUserRepository userRepository,
    IPasswordHasher passwordHasher,
    ITokenService tokenService) : IAuthService
{
    private const string DummyPasswordHash =
        "$argon2id$v=19$m=65536,t=3,p=4$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    /// <inheritdoc />
    public async Task<ServiceResult<AuthSession>> LoginAsync(
        string identifier,
        string password,
        CancellationToken cancellationToken)
    {
        var usernameLookup = userRepository.GetByUsernameAsync(identifier, cancellationToken);
        var emailLookup = userRepository.GetByEmailAsync(identifier, cancellationToken);
        await Task.WhenAll(usernameLookup, emailLookup);

        var user = await usernameLookup ?? await emailLookup;
        var passwordMatches = passwordHasher.VerifyPassword(
            password,
            user?.PasswordHash ?? DummyPasswordHash);

        if (user is null ||
            !user.IsActive ||
            !passwordMatches)
        {
            return ServiceResult<AuthSession>.Failure(
                ServiceError.Unauthorized,
                "帳號或密碼錯誤");
        }

        var session = CreateSession(user);
        await userRepository.AddRefreshTokenAsync(
            user.Id,
            session.PersistedRefreshToken,
            cancellationToken);

        return ServiceResult<AuthSession>.Success(session.Session);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<AuthSession>> RefreshAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
        {
            return ServiceResult<AuthSession>.Failure(
                ServiceError.Unauthorized,
                "缺少登入憑證");
        }

        var refreshTokenHash = tokenService.HashRefreshToken(rawRefreshToken);
        var user = await userRepository.GetByRefreshTokenHashAsync(
            refreshTokenHash,
            cancellationToken);
        if (user is null || !user.IsActive)
        {
            return ServiceResult<AuthSession>.Failure(
                ServiceError.Unauthorized,
                "使用者不存在或已停用");
        }

        var currentRefreshToken = user.RefreshTokens
            .FirstOrDefault(token => token.TokenHash == refreshTokenHash);
        if (currentRefreshToken is null)
        {
            return ServiceResult<AuthSession>.Failure(
                ServiceError.Unauthorized,
                "Refresh Token 無效或已過期");
        }

        if (!currentRefreshToken.IsActive)
        {
            if (currentRefreshToken.IsRevoked &&
                currentRefreshToken.ReplacedByTokenHash is not null)
            {
                await userRepository.RevokeTokenFamilyAsync(
                    user.Id,
                    currentRefreshToken.FamilyId,
                    cancellationToken);
            }

            return ServiceResult<AuthSession>.Failure(
                ServiceError.Unauthorized,
                "Refresh Token 無效或已過期");
        }

        var generatedAccessToken = tokenService.GenerateAccessToken(user);
        var generatedRefreshToken = tokenService.GenerateRefreshToken(currentRefreshToken.FamilyId);
        var rotated = await userRepository.RotateRefreshTokenAsync(
            user.Id,
            refreshTokenHash,
            generatedRefreshToken.PersistedToken,
            cancellationToken);

        if (!rotated)
        {
            await userRepository.RevokeTokenFamilyAsync(
                user.Id,
                currentRefreshToken.FamilyId,
                cancellationToken);
            return ServiceResult<AuthSession>.Failure(
                ServiceError.Unauthorized,
                "登入工作階段已失效");
        }

        return ServiceResult<AuthSession>.Success(new AuthSession(
            generatedAccessToken.Token,
            generatedRefreshToken.Token,
            generatedAccessToken.ExpiresAt,
            generatedRefreshToken.PersistedToken.ExpiresAt,
            ToUserInfo(user)));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> LogoutAsync(
        string? userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<bool>.Failure(ServiceError.Unauthorized, "未授權");
        }

        await userRepository.RevokeAllRefreshTokensAsync(userId, cancellationToken);
        return ServiceResult<bool>.Success(true);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserInfo>> GetUserAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ServiceResult<UserInfo>.Failure(ServiceError.Unauthorized, "未授權");
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        return user is null
            ? ServiceResult<UserInfo>.Failure(ServiceError.NotFound, "使用者不存在")
            : ServiceResult<UserInfo>.Success(ToUserInfo(user));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<UserInfo>> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        if (await userRepository.ExistsAsync(
                user => user.Username == request.Username,
                cancellationToken))
        {
            return ServiceResult<UserInfo>.Failure(ServiceError.Validation, "帳號已存在");
        }

        if (await userRepository.ExistsAsync(
                user => user.Email == request.Email,
                cancellationToken))
        {
            return ServiceResult<UserInfo>.Failure(ServiceError.Validation, "信箱已被使用");
        }

        if (!Enum.TryParse<UserRole>(request.Role, true, out var role) ||
            !Enum.IsDefined(role))
        {
            return ServiceResult<UserInfo>.Failure(ServiceError.Validation, "無效的角色");
        }

        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            PasswordHash = passwordHasher.HashPassword(request.Password),
            Role = role,
            DisplayName = request.DisplayName,
            IsActive = true
        };

        await userRepository.CreateAsync(user, cancellationToken);
        return ServiceResult<UserInfo>.Success(ToUserInfo(user));
    }

    private SessionMaterial CreateSession(User user)
    {
        var accessToken = tokenService.GenerateAccessToken(user);
        var refreshToken = tokenService.GenerateRefreshToken();
        return new SessionMaterial(
            new AuthSession(
                accessToken.Token,
                refreshToken.Token,
                accessToken.ExpiresAt,
                refreshToken.PersistedToken.ExpiresAt,
                ToUserInfo(user)),
            refreshToken.PersistedToken);
    }

    private static UserInfo ToUserInfo(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Email = user.Email,
        DisplayName = user.DisplayName,
        Role = user.Role.ToString(),
        LinkedMemberId = user.LinkedMemberId
    };

    private sealed record SessionMaterial(
        AuthSession Session,
        Domain.ValueObjects.RefreshToken PersistedRefreshToken);
}
