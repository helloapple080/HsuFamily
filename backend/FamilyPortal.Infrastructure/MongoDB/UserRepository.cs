using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.ValueObjects;
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
    public async Task<User?> GetByRefreshTokenAsync(string token)
    {
        return await _collection
            .Find(u => u.RefreshToken != null && u.RefreshToken.Token == token)
            .FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task UpdateRefreshTokenAsync(string userId, RefreshToken? refreshToken)
    {
        var update = Builders<User>.Update
            .Set(u => u.RefreshToken, refreshToken)
            .Set(u => u.UpdatedAt, DateTime.UtcNow);

        await _collection.UpdateOneAsync(u => u.Id == userId, update);
    }
}
