using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Infrastructure.Security;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證 JWT 與 Refresh Token 的安全屬性。</summary>
public class JwtTokenServiceTests
{
    private readonly JwtTokenService _tokenService = new(Options.Create(new JwtSettings
    {
        SecretKey = "unit-test-secret-key-with-at-least-32-characters",
        Issuer = "FamilyPortal.Tests",
        Audience = "FamilyPortal.Tests.Client",
        AccessTokenExpirationMinutes = 5,
        RefreshTokenExpirationDays = 1
    }));

    /// <summary>Refresh Token 的原始值不得出現在持久化模型。</summary>
    [Fact]
    public void GenerateRefreshToken_PersistsOnlyHash()
    {
        var generated = _tokenService.GenerateRefreshToken();

        Assert.NotEmpty(generated.Token);
        Assert.NotEqual(generated.Token, generated.PersistedToken.TokenHash);
        Assert.Equal(
            _tokenService.HashRefreshToken(generated.Token),
            generated.PersistedToken.TokenHash);
    }

    /// <summary>輪替後的 Refresh Token 必須保留相同 token family。</summary>
    [Fact]
    public void GenerateRefreshToken_WithFamilyId_PreservesFamily()
    {
        var first = _tokenService.GenerateRefreshToken();
        var rotated = _tokenService.GenerateRefreshToken(first.PersistedToken.FamilyId);

        Assert.Equal(first.PersistedToken.FamilyId, rotated.PersistedToken.FamilyId);
        Assert.NotEqual(first.Token, rotated.Token);
        Assert.NotEqual(first.PersistedToken.TokenHash, rotated.PersistedToken.TokenHash);
    }

    /// <summary>有效 JWT 應可安全取回使用者 ID。</summary>
    [Fact]
    public void AccessToken_RoundTrip_PreservesUserId()
    {
        var user = new User
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Username = "security-test",
            Email = "security-test@example.com",
            DisplayName = "安全測試",
            Role = UserRole.Admin
        };

        var token = _tokenService.GenerateAccessToken(user);

        Assert.Equal(user.Id, _tokenService.GetUserIdFromExpiredToken(token));
    }
}
