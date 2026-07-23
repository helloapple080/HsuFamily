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
    public UserRepository(MongoDbContext context) : base(context, "Users") { }

    /// <inheritdoc />
    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _collection.Find(u => u.Username == username).FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _collection.Find(u => u.Email == email).FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<User?> GetByRefreshTokenHashAsync(string tokenHash)
    {
        return await _collection.Find(
            Builders<User>.Filter.ElemMatch(
                user => user.RefreshTokens,
                token => token.TokenHash == tokenHash))
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task ReplaceRefreshTokensAsync(
        string userId,
        IReadOnlyCollection<RefreshToken> refreshTokens)
    {
        var update = Builders<User>.Update
            .Set(u => u.RefreshTokens, refreshTokens.ToList())
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        await _collection.UpdateOneAsync(u => u.Id == userId, update);
    }

    /// <inheritdoc />
    public async Task<bool> RotateRefreshTokenAsync(
        string userId,
        string currentTokenHash,
        RefreshToken newRefreshToken)
    {
        var now = DateTime.UtcNow;
        var filter = Builders<User>.Filter.And(
            Builders<User>.Filter.Eq(u => u.Id, userId),
            Builders<User>.Filter.ElemMatch(
                u => u.RefreshTokens,
                token => token.TokenHash == currentTokenHash &&
                         token.RevokedAt == null &&
                         token.ExpiresAt > now));

        var update = Builders<User>.Update
            .Set("refreshTokens.$.revokedAt", now)
            .Set("refreshTokens.$.replacedByTokenHash", newRefreshToken.TokenHash)
            .Push(u => u.RefreshTokens, newRefreshToken)
            .Set(u => u.UpdatedAt, now);

        var result = await _collection.UpdateOneAsync(filter, update);
        return result.ModifiedCount == 1;
    }

    /// <inheritdoc />
    public async Task RevokeTokenFamilyAsync(string userId, string familyId)
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

        await _collection.UpdateOneAsync(filter, update, options);
    }

    /// <inheritdoc />
    public async Task RevokeAllRefreshTokensAsync(string userId)
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

        await _collection.UpdateOneAsync(filter, update, options);
    }
}
