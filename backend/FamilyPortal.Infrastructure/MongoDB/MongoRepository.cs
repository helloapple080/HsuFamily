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
    public async Task<T?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<T>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await _collection.Find(_ => true).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<T>> FindAsync(
        Expression<Func<T, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        return await _collection.Find(filter).ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<T?> FindOneAsync(
        Expression<Func<T, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CreateAsync(T entity, CancellationToken cancellationToken = default)
    {
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;
        await _collection.InsertOneAsync(entity, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task UpdateAsync(string id, T entity, CancellationToken cancellationToken = default)
    {
        entity.UpdatedAt = DateTime.UtcNow;
        await _collection.ReplaceOneAsync(
            x => x.Id == id,
            entity,
            options: null,
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _collection.DeleteOneAsync(x => x.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(
        Expression<Func<T, bool>> filter,
        CancellationToken cancellationToken = default)
    {
        return await _collection.Find(filter).AnyAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> CountAsync(
        Expression<Func<T, bool>>? filter = null,
        CancellationToken cancellationToken = default)
    {
        return filter == null
            ? await _collection.CountDocumentsAsync(_ => true, cancellationToken: cancellationToken)
            : await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
    }
}
