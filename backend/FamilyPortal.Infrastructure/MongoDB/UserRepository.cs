using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.ValueObjects;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FamilyPortal.Infrastructure.MongoDB;

/// <summary>
/// 使用者 Repository 實作 — 包含 JWT Token 管理相關操作
/// </summary>
public class UserRepository : MongoRepository<User>, IUserRepository
{
    private const int MaxRefreshTokenHistory = 50;

    public UserRepository(MongoDbContext context) : base(context, "Users") { }

    /// <inheritdoc />
    public async Task<User?> GetByUsernameAsync(
        string username,
        CancellationToken cancellationToken = default)
    {
        return await _collection.Find(u => u.Username == username)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        return await _collection.Find(u => u.Email == email)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<User?> GetByRefreshTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken = default)
    {
        return await _collection.Find(
            Builders<User>.Filter.ElemMatch(
                user => user.RefreshTokens,
                token => token.TokenHash == tokenHash))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task AddRefreshTokenAsync(
        string userId,
        RefreshToken refreshToken,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var update = BuildAddRefreshTokenUpdate(refreshToken, now);

        await _collection.UpdateOneAsync(
            u => u.Id == userId,
            update,
            cancellationToken: cancellationToken);
    }

    private static UpdateDefinition<User> BuildAddRefreshTokenUpdate(
        RefreshToken refreshToken,
        DateTime now)
    {
        var appendStage = new BsonDocument(
            "$set",
            new BsonDocument(
                "refreshTokens",
                new BsonDocument(
                    "$concatArrays",
                    new BsonArray
                    {
                        ExistingRefreshTokens(),
                        LiteralTokenArray(refreshToken)
                    })));

        return BuildRefreshTokenPipeline(appendStage, now);
    }

    /// <inheritdoc />
    public async Task<bool> RotateRefreshTokenAsync(
        string userId,
        string currentTokenHash,
        RefreshToken newRefreshToken,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var filter = BuildActiveRefreshTokenFilter(userId, currentTokenHash, now);
        var update = BuildRefreshTokenRotationUpdate(
            currentTokenHash,
            newRefreshToken,
            now);

        var result = await _collection.UpdateOneAsync(
            filter,
            update,
            options: null,
            cancellationToken: cancellationToken);
        return result.ModifiedCount == 1;
    }

    private static FilterDefinition<User> BuildActiveRefreshTokenFilter(
        string userId,
        string currentTokenHash,
        DateTime now) => Builders<User>.Filter.And(
            Builders<User>.Filter.Eq(u => u.Id, userId),
            Builders<User>.Filter.ElemMatch(
                u => u.RefreshTokens,
                token => token.TokenHash == currentTokenHash &&
                         token.RevokedAt == null &&
                         token.ExpiresAt > now));

    private static UpdateDefinition<User> BuildRefreshTokenRotationUpdate(
        string currentTokenHash,
        RefreshToken newRefreshToken,
        DateTime now)
    {
        var mappedTokens = new BsonDocument(
            "$map",
            new BsonDocument
            {
                { "input", ExistingRefreshTokens() },
                { "as", "token" },
                {
                    "in",
                    new BsonDocument(
                        "$cond",
                        new BsonArray
                        {
                            new BsonDocument(
                                "$eq",
                                new BsonArray
                                {
                                    "$$token.tokenHash",
                                    Literal(currentTokenHash)
                                }),
                            new BsonDocument(
                                "$mergeObjects",
                                new BsonArray
                                {
                                    "$$token",
                                    new BsonDocument
                                    {
                                        { "revokedAt", new BsonDateTime(now) },
                                        {
                                            "replacedByTokenHash",
                                            Literal(newRefreshToken.TokenHash)
                                        }
                                    }
                                }),
                            "$$token"
                        })
                }
            });
        var rotateStage = new BsonDocument(
            "$set",
            new BsonDocument(
                "refreshTokens",
                new BsonDocument(
                    "$concatArrays",
                    new BsonArray
                    {
                        mappedTokens,
                        LiteralTokenArray(newRefreshToken)
                    })));

        return BuildRefreshTokenPipeline(rotateStage, now);
    }

    private static UpdateDefinition<User> BuildRefreshTokenPipeline(
        BsonDocument mutationStage,
        DateTime now)
    {
        PipelineDefinition<User, User> pipeline = new BsonDocument[]
        {
            mutationStage,
            BuildRefreshTokenRetentionStage(now)
        };
        return Builders<User>.Update.Pipeline(pipeline);
    }

    private static BsonDocument BuildRefreshTokenRetentionStage(DateTime now)
    {
        var activeTokens = new BsonDocument(
            "$filter",
            new BsonDocument
            {
                { "input", "$$tokens" },
                { "as", "token" },
                { "cond", ActiveTokenCondition(now) }
            });
        var inactiveTokens = new BsonDocument(
            "$filter",
            new BsonDocument
            {
                { "input", "$$tokens" },
                { "as", "token" },
                {
                    "cond",
                    new BsonDocument(
                        "$not",
                        new BsonArray { ActiveTokenCondition(now) })
                }
            });
        var newestInactiveTokens = new BsonDocument(
            "$slice",
            new BsonArray
            {
                new BsonDocument(
                    "$sortArray",
                    new BsonDocument
                    {
                        { "input", inactiveTokens },
                        {
                            "sortBy",
                            new BsonDocument
                            {
                                { "createdAt", 1 },
                                { "revokedAt", 1 }
                            }
                        }
                    }),
                -MaxRefreshTokenHistory
            });

        return new BsonDocument(
            "$set",
            new BsonDocument
            {
                {
                    "refreshTokens",
                    new BsonDocument(
                        "$let",
                        new BsonDocument
                        {
                            {
                                "vars",
                                new BsonDocument("tokens", ExistingRefreshTokens())
                            },
                            {
                                "in",
                                new BsonDocument(
                                    "$concatArrays",
                                    new BsonArray
                                    {
                                        activeTokens,
                                        newestInactiveTokens
                                    })
                            }
                        })
                },
                { "updatedAt", new BsonDateTime(now) }
            });
    }

    private static BsonDocument ActiveTokenCondition(DateTime now) =>
        new(
            "$and",
            new BsonArray
            {
                new BsonDocument(
                    "$eq",
                    new BsonArray
                    {
                        new BsonDocument(
                            "$ifNull",
                            new BsonArray { "$$token.revokedAt", BsonNull.Value }),
                        BsonNull.Value
                    }),
                new BsonDocument(
                    "$gt",
                    new BsonArray
                    {
                        "$$token.expiresAt",
                        new BsonDateTime(now)
                    })
            });

    private static BsonDocument ExistingRefreshTokens() =>
        new("$ifNull", new BsonArray { "$refreshTokens", new BsonArray() });

    private static BsonDocument Literal(string value) =>
        new("$literal", value);

    private static BsonArray LiteralTokenArray(RefreshToken token) =>
        new()
        {
            new BsonDocument("$literal", token.ToBsonDocument())
        };

    /// <inheritdoc />
    public async Task RevokeTokenFamilyAsync(
        string userId,
        string familyId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<User>.Filter.Eq(u => u.Id, userId);
        var update = Builders<User>.Update
            .Set("refreshTokens.$[token].revokedAt", now)
            .Set(u => u.UpdatedAt, now);
        var options = new UpdateOptions
        {
            ArrayFilters =
            [
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument
                    {
                        { "token.familyId", familyId },
                        { "token.revokedAt", BsonNull.Value }
                    })
            ]
        };

        await _collection.UpdateOneAsync(filter, update, options, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RevokeAllRefreshTokensAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<User>.Filter.Eq(u => u.Id, userId);
        var update = Builders<User>.Update
            .Set("refreshTokens.$[token].revokedAt", now)
            .Set(u => u.UpdatedAt, now);
        var options = new UpdateOptions
        {
            ArrayFilters =
            [
                new BsonDocumentArrayFilterDefinition<BsonDocument>(
                    new BsonDocument("token.revokedAt", BsonNull.Value))
            ]
        };

        await _collection.UpdateOneAsync(filter, update, options, cancellationToken);
    }
}
