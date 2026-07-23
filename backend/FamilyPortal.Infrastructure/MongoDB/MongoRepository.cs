using System.Linq.Expressions;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using MongoDB.Driver;

namespace FamilyPortal.Infrastructure.MongoDB;

/// <summary>
/// 泛型 Repository 實作 — 提供所有 Collection 共用的 CRUD 操作
/// 使用 MongoDB Driver 的強型別 API
/// </summary>
/// <typeparam name="T">繼承自 BaseEntity 的實體型別</typeparam>
public class MongoRepository<T> : IRepository<T> where T : BaseEntity
{
    protected readonly IMongoCollection<T> _collection;

    public MongoRepository(MongoDbContext context, string collectionName)
    {
        _collection = context.GetCollection<T>(collectionName);
    }

    /// <inheritdoc />
    public async Task<T?> GetByIdAsync(string id)
    {
        return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task<List<T>> GetAllAsync()
    {
        return await _collection.Find(_ => true).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> filter)
    {
        return await _collection.Find(filter).ToListAsync();
    }

    /// <inheritdoc />
    public async Task<T?> FindOneAsync(Expression<Func<T, bool>> filter)
    {
        return await _collection.Find(filter).FirstOrDefaultAsync();
    }

    /// <inheritdoc />
    public async Task CreateAsync(T entity)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _collection.InsertOneAsync(entity);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(string id, T entity)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(x => x.Id == id, entity);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id)
    {
        await _collection.DeleteOneAsync(x => x.Id == id);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Expression<Func<T, bool>> filter)
    {
        return await _collection.Find(filter).AnyAsync();
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(Expression<Func<T, bool>>? filter = null)
    {
        return filter == null
            ? await _collection.CountDocumentsAsync(_ => true)
            : await _collection.CountDocumentsAsync(filter);
    }
}
