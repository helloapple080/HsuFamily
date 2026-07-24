using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Domain.ValueObjects;
using FamilyPortal.Infrastructure.MongoDB;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Xunit;

namespace FamilyPortal.Security.Tests;

/// <summary>驗證預設管理員初始化具備併發與錯誤傳播保護。</summary>
public sealed class AdminBootstrapTests
{
    /// <summary>已有管理員時應略過新增。</summary>
    [Fact]
    public async Task EnsureAsync_ExistingAdminSkipsInsert()
    {
        var repository = new FakeUserRepository(true);
        using var source = new CancellationTokenSource();
        var factoryCalls = 0;

        var result = await DefaultAdminBootstrap.EnsureAsync(
            repository,
            () =>
            {
                factoryCalls++;
                return DefaultAdmin();
            },
            source.Token);

        Assert.Equal(DefaultAdminBootstrapResult.Existing, result);
        Assert.Equal(0, repository.TryCreateCalls);
        Assert.Equal(0, factoryCalls);
        Assert.All(repository.CancellationTokens, token => Assert.Equal(source.Token, token));
    }

    /// <summary>另一個 instance 先建立管理員時應安全視為已存在。</summary>
    [Fact]
    public async Task EnsureAsync_DuplicateRaceWithAdminIsSafeSkip()
    {
        var repository = new FakeUserRepository(false, true)
        {
            TryCreateResult = false
        };
        using var source = new CancellationTokenSource();
        var factoryCalls = 0;

        var result = await DefaultAdminBootstrap.EnsureAsync(
            repository,
            () =>
            {
                factoryCalls++;
                return DefaultAdmin();
            },
            source.Token);

        Assert.Equal(DefaultAdminBootstrapResult.Existing, result);
        Assert.Equal(1, repository.TryCreateCalls);
        Assert.Equal(2, repository.ExistsCalls);
        Assert.Equal(1, factoryCalls);
        Assert.All(repository.CancellationTokens, token => Assert.Equal(source.Token, token));
    }

    /// <summary>唯一識別值與非管理員衝突時應停止啟動，不可誤報已有管理員。</summary>
    [Fact]
    public async Task EnsureAsync_DuplicateWithoutAdminThrowsCollision()
    {
        var repository = new FakeUserRepository(false, false)
        {
            TryCreateResult = false
        };
        var factoryCalls = 0;

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DefaultAdminBootstrap.EnsureAsync(
                repository,
                () =>
                {
                    factoryCalls++;
                    return DefaultAdmin();
                },
                TestContext.Current.CancellationToken));

        Assert.Contains("與現有非管理員帳號衝突", exception.Message);
        Assert.Equal(2, repository.ExistsCalls);
        Assert.Equal(1, factoryCalls);
    }

    /// <summary>非 duplicate-key 錯誤不得由 bootstrap helper 吞掉。</summary>
    [Fact]
    public async Task EnsureAsync_NonDuplicateFailurePropagates()
    {
        var expected = new InvalidDataException("database unavailable");
        var repository = new FakeUserRepository(false)
        {
            TryCreateException = expected
        };
        var factoryCalls = 0;

        var actual = await Assert.ThrowsAsync<InvalidDataException>(() =>
            DefaultAdminBootstrap.EnsureAsync(
                repository,
                () =>
                {
                    factoryCalls++;
                    return DefaultAdmin();
                },
                TestContext.Current.CancellationToken));

        Assert.Same(expected, actual);
        Assert.Equal(1, repository.ExistsCalls);
        Assert.Equal(1, factoryCalls);
    }

    /// <summary>沒有管理員且 insert 成功時應回傳已建立。</summary>
    [Fact]
    public async Task EnsureAsync_SuccessfulInsertReturnsCreated()
    {
        var candidate = DefaultAdmin();
        var repository = new FakeUserRepository(false)
        {
            TryCreateResult = true
        };
        var factoryCalls = 0;

        var result = await DefaultAdminBootstrap.EnsureAsync(
            repository,
            () =>
            {
                factoryCalls++;
                return candidate;
            },
            TestContext.Current.CancellationToken);

        Assert.Equal(DefaultAdminBootstrapResult.Created, result);
        Assert.Same(candidate, repository.CreatedCandidate);
        Assert.Equal(1, repository.ExistsCalls);
        Assert.Equal(1, factoryCalls);
    }

    /// <summary>Repository duplicate-key seam 應回傳 false。</summary>
    [Fact]
    public async Task DuplicateSafeInsert_DuplicateKeyReturnsFalse()
    {
        var exception = MongoWriteError(ServerErrorCategory.DuplicateKey);

        var created = await ExecuteDuplicateSafeInsertAsync(_ => Task.FromException(exception));

        Assert.False(created);
    }

    /// <summary>Repository duplicate-key seam 不得吞掉其他 Mongo write 錯誤。</summary>
    [Fact]
    public async Task DuplicateSafeInsert_NonDuplicateMongoErrorPropagates()
    {
        var expected = MongoWriteError(ServerErrorCategory.Uncategorized);

        var actual = await Assert.ThrowsAsync<MongoWriteException>(() =>
            ExecuteDuplicateSafeInsertAsync(_ => Task.FromException(expected)));

        Assert.Same(expected, actual);
    }

    /// <summary>Repository duplicate-key seam 不得吞掉任意非 Mongo 例外。</summary>
    [Fact]
    public async Task DuplicateSafeInsert_ArbitraryErrorPropagates()
    {
        var expected = new InvalidDataException("insert failed");

        var actual = await Assert.ThrowsAsync<InvalidDataException>(() =>
            ExecuteDuplicateSafeInsertAsync(_ => Task.FromException(expected)));

        Assert.Same(expected, actual);
    }

    /// <summary>Repository duplicate-key seam 應把相同取消權杖傳給 insert delegate。</summary>
    [Fact]
    public async Task DuplicateSafeInsert_ForwardsCancellationToken()
    {
        using var source = new CancellationTokenSource();
        var observedToken = CancellationToken.None;

        var created = await ExecuteDuplicateSafeInsertAsync(token =>
        {
            observedToken = token;
            return Task.CompletedTask;
        }, source.Token);

        Assert.True(created);
        Assert.Equal(source.Token, observedToken);
    }

    /// <summary>TryCreateAsync 必須透過可測試 seam 執行實際 insert。</summary>
    [Fact]
    public void TryCreateAsync_UsesDuplicateSafeInsertSeam()
    {
        var source = File.ReadAllText(SourcePath(
            "backend", "FamilyPortal.Infrastructure", "MongoDB", "UserRepository.cs"));

        Assert.Contains("return await ExecuteDuplicateSafeInsertAsync(", source);
        Assert.Contains("_collection.InsertOneAsync(entity, cancellationToken: token)", source);
    }

    private static User DefaultAdmin() => new()
    {
        Username = "admin",
        Email = "admin@family.portal",
        Role = UserRole.Admin
    };

    private static async Task<bool> ExecuteDuplicateSafeInsertAsync(
        Func<CancellationToken, Task> insert,
        CancellationToken cancellationToken = default)
    {
        var method = typeof(UserRepository).GetMethod(
            "ExecuteDuplicateSafeInsertAsync",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        var task = method.Invoke(null, [insert, cancellationToken]) as Task<bool>;
        Assert.NotNull(task);
        return await task;
    }

    private static MongoWriteException MongoWriteError(ServerErrorCategory category)
    {
        var constructor = typeof(WriteError).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(ServerErrorCategory), typeof(int), typeof(string), typeof(BsonDocument)],
            modifiers: null);
        Assert.NotNull(constructor);
        var writeError = constructor.Invoke(
            [category, category == ServerErrorCategory.DuplicateKey ? 11000 : 8, "write failed", new BsonDocument()])
            as WriteError;
        Assert.NotNull(writeError);

        var connectionId = new ConnectionId(
            new ServerId(new ClusterId(), new DnsEndPoint("localhost", 27017)));
        return new MongoWriteException(connectionId, writeError!, null, null);
    }

    private static string SourcePath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "backend", "FamilyPortal.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine([directory!.FullName, .. segments]);
    }

    private sealed class FakeUserRepository(params bool[] existsResults) : IUserRepository
    {
        private readonly Queue<bool> _existsResults = new(existsResults);

        public bool TryCreateResult { get; init; }
        public Exception? TryCreateException { get; init; }
        public int TryCreateCalls { get; private set; }
        public int ExistsCalls { get; private set; }
        public User? CreatedCandidate { get; private set; }
        public List<CancellationToken> CancellationTokens { get; } = [];

        /// <inheritdoc />
        public Task<bool> TryCreateAsync(User user, CancellationToken cancellationToken = default)
        {
            TryCreateCalls++;
            CreatedCandidate = user;
            CancellationTokens.Add(cancellationToken);
            return TryCreateException is null
                ? Task.FromResult(TryCreateResult)
                : Task.FromException<bool>(TryCreateException);
        }

        /// <inheritdoc />
        public Task<bool> ExistsAsync(
            Expression<Func<User, bool>> filter,
            CancellationToken cancellationToken = default)
        {
            ExistsCalls++;
            CancellationTokens.Add(cancellationToken);
            Assert.True(filter.Compile()(new User { Role = UserRole.Admin }));
            Assert.False(filter.Compile()(new User { Role = UserRole.User }));
            return Task.FromResult(_existsResults.Dequeue());
        }

        /// <inheritdoc />
        public Task<User?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<User?> GetByRefreshTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task AddRefreshTokenAsync(string userId, RefreshToken refreshToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<bool> RotateRefreshTokenAsync(string userId, string currentTokenHash, RefreshToken newRefreshToken, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task RevokeTokenFamilyAsync(string userId, string familyId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task RevokeAllRefreshTokensAsync(string userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<User?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<List<User>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<List<User>> FindAsync(Expression<Func<User, bool>> filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<User?> FindOneAsync(Expression<Func<User, bool>> filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task CreateAsync(User entity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task UpdateAsync(string id, User entity, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task DeleteAsync(string id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        /// <inheritdoc />
        public Task<long> CountAsync(Expression<Func<User, bool>>? filter = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
