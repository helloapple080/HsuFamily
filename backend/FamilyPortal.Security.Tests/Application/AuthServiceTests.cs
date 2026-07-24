using System.Linq.Expressions;
using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Auth;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Services;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Domain.ValueObjects;
using Xunit;

namespace FamilyPortal.Security.Tests.Application;

/// <summary>驗證認證 use case 的安全錯誤、Token 輪替與公開資料映射。</summary>
public sealed class AuthServiceTests
{
    /// <summary>不存在的帳號與錯誤密碼必須使用相同的未授權訊息。</summary>
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task LoginAsync_InvalidCredentialsReturnGenericUnauthorized(
        bool userExists,
        bool passwordMatches)
    {
        var repository = new FakeUserRepository
        {
            User = userExists ? ActiveUser() : null
        };
        var passwordHasher = new FakePasswordHasher { VerifyResult = passwordMatches };
        var service = new AuthService(repository, passwordHasher, new FakeTokenService());

        var result = await service.LoginAsync("member", "wrong", CancellationToken.None);

        Assert.Equal(ServiceError.Unauthorized, result.Error);
        Assert.Equal("帳號或密碼錯誤", result.Message);
        Assert.Equal(1, repository.UsernameLookupCalls);
        Assert.Equal(1, repository.EmailLookupCalls);
        Assert.Equal(1, passwordHasher.VerifyCalls);
        if (userExists)
        {
            Assert.Equal("stored-hash", passwordHasher.LastVerifiedHash);
        }
        else
        {
            Assert.StartsWith("$argon2id$", passwordHasher.LastVerifiedHash);
        }
        Assert.Null(repository.AddedToken);
    }

    /// <summary>登入成功只能由工作階段攜帶原始 Token，公開使用者不得含密碼資料。</summary>
    [Fact]
    public async Task LoginAsync_AppendsHashedRefreshTokenAndReturnsSessionExpiry()
    {
        var repository = new FakeUserRepository { User = ActiveUser() };
        var tokenService = new FakeTokenService();
        var passwordHasher = new FakePasswordHasher();
        using var source = new CancellationTokenSource();
        var service = new AuthService(repository, passwordHasher, tokenService);

        var result = await service.LoginAsync("member", "correct", source.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("raw-access", result.Value!.AccessToken);
        Assert.Equal("raw-refresh", result.Value.RefreshToken);
        Assert.Equal(tokenService.AccessExpiresAt, result.Value.AccessTokenExpiresAt);
        Assert.Equal(tokenService.RefreshExpiresAt, result.Value.RefreshTokenExpiresAt);
        Assert.Equal("member", result.Value.User.Username);
        Assert.Equal("hash-new-refresh", repository.AddedToken!.TokenHash);
        Assert.DoesNotContain("raw-refresh", repository.AddedToken.TokenHash);
        Assert.Equal(1, repository.UsernameLookupCalls);
        Assert.Equal(1, repository.EmailLookupCalls);
        Assert.Equal(1, passwordHasher.VerifyCalls);
        Assert.Equal(source.Token, repository.LastCancellationToken);
    }

    /// <summary>已輪替 Token 被重放時必須撤銷整個 family。</summary>
    [Fact]
    public async Task RefreshAsync_ReplayedTokenRevokesFamily()
    {
        var token = new RefreshToken
        {
            TokenHash = "hash-refresh",
            FamilyId = "family-1",
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            RevokedAt = DateTime.UtcNow.AddMinutes(-1),
            ReplacedByTokenHash = "replacement-hash"
        };
        var repository = new FakeUserRepository { User = ActiveUser(token) };
        var service = new AuthService(
            repository,
            new FakePasswordHasher(),
            new FakeTokenService());

        var result = await service.RefreshAsync("raw-refresh", CancellationToken.None);

        Assert.Equal(ServiceError.Unauthorized, result.Error);
        Assert.Equal("Refresh Token 無效或已過期", result.Message);
        Assert.Equal("family-1", repository.RevokedFamilyId);
    }

    /// <summary>輪替競爭失敗時必須撤銷 family 並使工作階段失效。</summary>
    [Fact]
    public async Task RefreshAsync_RotationRaceRevokesFamily()
    {
        var repository = new FakeUserRepository
        {
            User = ActiveUser(ActiveRefreshToken()),
            RotationResult = false
        };
        var service = new AuthService(
            repository,
            new FakePasswordHasher(),
            new FakeTokenService());

        var result = await service.RefreshAsync("raw-refresh", CancellationToken.None);

        Assert.Equal(ServiceError.Unauthorized, result.Error);
        Assert.Equal("登入工作階段已失效", result.Message);
        Assert.Equal("family-1", repository.RevokedFamilyId);
    }

    /// <summary>有效 Refresh Token 應原子輪替並保留 family。</summary>
    [Fact]
    public async Task RefreshAsync_ActiveTokenRotatesAndReturnsNewSession()
    {
        var repository = new FakeUserRepository { User = ActiveUser(ActiveRefreshToken()) };
        using var source = new CancellationTokenSource();
        var service = new AuthService(
            repository,
            new FakePasswordHasher(),
            new FakeTokenService());

        var result = await service.RefreshAsync("raw-refresh", source.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("hash-refresh", repository.RotatedCurrentHash);
        Assert.Equal("family-1", repository.RotatedToken!.FamilyId);
        Assert.Equal(source.Token, repository.LastCancellationToken);
    }

    /// <summary>登出必須撤銷所有 Token 並傳遞取消權杖。</summary>
    [Fact]
    public async Task LogoutAsync_AuthenticatedUserRevokesAllTokens()
    {
        var repository = new FakeUserRepository();
        using var source = new CancellationTokenSource();
        var service = new AuthService(
            repository,
            new FakePasswordHasher(),
            new FakeTokenService());

        var result = await service.LogoutAsync("user-1", source.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("user-1", repository.RevokedAllUserId);
        Assert.Equal(source.Token, repository.LastCancellationToken);
    }

    /// <summary>註冊必須拒絕重複帳號，且不得進行寫入。</summary>
    [Fact]
    public async Task RegisterAsync_DuplicateUsernameReturnsConflictWithoutWrite()
    {
        var repository = new FakeUserRepository { UsernameExists = true };
        var service = new AuthService(
            repository,
            new FakePasswordHasher(),
            new FakeTokenService());

        var result = await service.RegisterAsync(ValidRegisterRequest(), CancellationToken.None);

        Assert.Equal(ServiceError.Validation, result.Error);
        Assert.Equal("帳號已存在", result.Message);
        Assert.Null(repository.Created);
    }

    /// <summary>註冊成功應使用密碼雜湊、解析角色並回傳無敏感欄位的使用者資訊。</summary>
    [Fact]
    public async Task RegisterAsync_ValidRequestCreatesHashedUser()
    {
        var repository = new FakeUserRepository();
        var passwordHasher = new FakePasswordHasher();
        using var source = new CancellationTokenSource();
        var service = new AuthService(repository, passwordHasher, new FakeTokenService());

        var result = await service.RegisterAsync(ValidRegisterRequest(), source.Token);

        Assert.True(result.IsSuccess);
        Assert.Equal("hashed-password", repository.Created!.PasswordHash);
        Assert.Equal(UserRole.Admin, repository.Created.Role);
        Assert.Equal(repository.Created.Id, result.Value!.Id);
        Assert.Equal(source.Token, repository.LastCancellationToken);
    }

    private static User ActiveUser(params RefreshToken[] refreshTokens) => new()
    {
        Id = "user-1",
        Username = "member",
        Email = "member@example.com",
        PasswordHash = "stored-hash",
        DisplayName = "家族成員",
        Role = UserRole.User,
        LinkedMemberId = "member-1",
        IsActive = true,
        RefreshTokens = [.. refreshTokens]
    };

    private static RefreshToken ActiveRefreshToken() => new()
    {
        TokenHash = "hash-refresh",
        FamilyId = "family-1",
        CreatedAt = DateTime.UtcNow.AddMinutes(-1),
        ExpiresAt = DateTime.UtcNow.AddHours(1)
    };

    private static RegisterRequest ValidRegisterRequest() => new()
    {
        Username = "new-admin",
        Email = "new-admin@example.com",
        Password = "secure-password",
        DisplayName = "新管理員",
        Role = "Admin"
    };

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        public bool VerifyResult { get; init; } = true;
        public int VerifyCalls { get; private set; }
        public string LastVerifiedHash { get; private set; } = string.Empty;

        /// <inheritdoc />
        public string HashPassword(string password) => "hashed-password";

        /// <inheritdoc />
        public bool VerifyPassword(string password, string hash)
        {
            VerifyCalls++;
            LastVerifiedHash = hash;
            return VerifyResult;
        }
    }

    private sealed class FakeTokenService : ITokenService
    {
        public DateTime AccessExpiresAt { get; } = DateTime.UtcNow.AddMinutes(5);
        public DateTime RefreshExpiresAt { get; } = DateTime.UtcNow.AddDays(1);

        /// <inheritdoc />
        public GeneratedAccessToken GenerateAccessToken(User user) =>
            new("raw-access", AccessExpiresAt);

        /// <inheritdoc />
        public GeneratedRefreshToken GenerateRefreshToken(string? familyId = null) => new()
        {
            Token = "raw-refresh",
            PersistedToken = new RefreshToken
            {
                TokenHash = "hash-new-refresh",
                FamilyId = familyId ?? "family-new",
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = RefreshExpiresAt
            }
        };

        /// <inheritdoc />
        public string HashRefreshToken(string token) => "hash-refresh";

        /// <inheritdoc />
        public string? GetUserIdFromExpiredToken(string token) => "user-1";
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public User? User { get; init; }
        public bool UsernameExists { get; init; }
        public bool EmailExists { get; init; }
        public bool RotationResult { get; init; } = true;
        public int UsernameLookupCalls { get; private set; }
        public int EmailLookupCalls { get; private set; }
        public RefreshToken? AddedToken { get; private set; }
        public string? RotatedCurrentHash { get; private set; }
        public RefreshToken? RotatedToken { get; private set; }
        public string? RevokedFamilyId { get; private set; }
        public string? RevokedAllUserId { get; private set; }
        public User? Created { get; private set; }
        public CancellationToken LastCancellationToken { get; private set; }

        /// <inheritdoc />
        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
        {
            UsernameLookupCalls++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(User?.Username == username ? User : null);
        }

        /// <inheritdoc />
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        {
            EmailLookupCalls++;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(User?.Email == email ? User : null);
        }

        /// <inheritdoc />
        public Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(User);
        }

        /// <inheritdoc />
        public Task AddRefreshTokenAsync(
            string userId,
            RefreshToken refreshToken,
            CancellationToken cancellationToken = default)
        {
            AddedToken = refreshToken;
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<bool> RotateRefreshTokenAsync(
            string userId,
            string currentTokenHash,
            RefreshToken newRefreshToken,
            CancellationToken cancellationToken = default)
        {
            RotatedCurrentHash = currentTokenHash;
            RotatedToken = newRefreshToken;
            LastCancellationToken = cancellationToken;
            return Task.FromResult(RotationResult);
        }

        /// <inheritdoc />
        public Task RevokeTokenFamilyAsync(
            string userId,
            string familyId,
            CancellationToken cancellationToken = default)
        {
            RevokedFamilyId = familyId;
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task RevokeAllRefreshTokensAsync(string userId, CancellationToken cancellationToken = default)
        {
            RevokedAllUserId = userId;
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            return Task.FromResult(User?.Id == id ? User : null);
        }

        /// <inheritdoc />
        public Task<List<User>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new List<User>());

        /// <inheritdoc />
        public Task<List<User>> FindAsync(
            Expression<Func<User, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult(new List<User>());

        /// <inheritdoc />
        public Task<User?> FindOneAsync(
            Expression<Func<User, bool>> filter,
            CancellationToken cancellationToken = default) => Task.FromResult<User?>(null);

        /// <inheritdoc />
        public Task CreateAsync(User entity, CancellationToken cancellationToken = default)
        {
            Created = entity;
            LastCancellationToken = cancellationToken;
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpdateAsync(string id, User entity, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        /// <inheritdoc />
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        /// <inheritdoc />
        public Task<bool> ExistsAsync(
            Expression<Func<User, bool>> filter,
            CancellationToken cancellationToken = default)
        {
            LastCancellationToken = cancellationToken;
            var probe = filter.Compile();
            var exists = UsernameExists && probe(new User { Username = "new-admin" }) ||
                         EmailExists && probe(new User { Email = "new-admin@example.com" });
            return Task.FromResult(exists);
        }

        /// <inheritdoc />
        public Task<long> CountAsync(
            Expression<Func<User, bool>>? filter = null,
            CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }
}
