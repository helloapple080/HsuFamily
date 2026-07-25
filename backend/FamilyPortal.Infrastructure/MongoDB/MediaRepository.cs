using FamilyPortal.Application.Common;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using MongoDB.Driver;

namespace FamilyPortal.Infrastructure.MongoDB;

/// <summary>媒體中繼資料的 MongoDB Repository。</summary>
public sealed class MediaRepository : IMediaRepository
{
    private readonly IMongoCollection<MediaAsset> _collection;

    /// <summary>建立媒體 Repository。</summary>
    public MediaRepository(MongoDbContext context)
    {
        _collection = context.GetCollection<MediaAsset>("MediaAssets");
    }

    /// <inheritdoc />
    public async Task<MediaAsset?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(BuildActiveIdFilter(id))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<long> CountActiveByOwnerAndPurposeAsync(
        string ownerEntityId,
        MediaPurpose purpose,
        CancellationToken cancellationToken = default)
    {
        return await _collection.CountDocumentsAsync(
            BuildActiveOwnerPurposeFilter(ownerEntityId, purpose),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task CreateAsync(
        MediaAsset asset,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        asset.CreatedAt = now;
        asset.UpdatedAt = now;
        await _collection.InsertOneAsync(asset, cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> SoftDeleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var result = await _collection.UpdateOneAsync(
            BuildSoftDeleteFilter(id),
            BuildSoftDeleteUpdate(DateTime.UtcNow),
            cancellationToken: cancellationToken);

        return result.ModifiedCount > 0;
    }

    /// <inheritdoc />
    public async Task MarkCleanupCompleteAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        await _collection.UpdateOneAsync(
            BuildCleanupCompleteFilter(id),
            BuildCleanupCompleteUpdate(DateTime.UtcNow),
            cancellationToken: cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<MediaAsset>> GetPendingCleanupAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        using var cursor = await _collection.FindAsync(
            BuildPendingCleanupFilter(),
            BuildPendingCleanupOptions(limit),
            cancellationToken);

        return await cursor.ToListAsync(cancellationToken);
    }

    private static FilterDefinition<MediaAsset> BuildActiveIdFilter(string id)
    {
        return Builders<MediaAsset>.Filter.And(
            Builders<MediaAsset>.Filter.Eq(asset => asset.Id, id),
            Builders<MediaAsset>.Filter.Eq(asset => asset.DeletedAt, null));
    }

    private static FilterDefinition<MediaAsset> BuildActiveOwnerPurposeFilter(
        string ownerEntityId,
        MediaPurpose purpose)
    {
        return Builders<MediaAsset>.Filter.And(
            Builders<MediaAsset>.Filter.Eq(asset => asset.OwnerEntityId, ownerEntityId),
            Builders<MediaAsset>.Filter.Eq(asset => asset.Purpose, purpose),
            Builders<MediaAsset>.Filter.Eq(asset => asset.DeletedAt, null));
    }

    private static FilterDefinition<MediaAsset> BuildSoftDeleteFilter(string id)
    {
        return BuildActiveIdFilter(id);
    }

    private static UpdateDefinition<MediaAsset> BuildSoftDeleteUpdate(DateTime deletedAt)
    {
        return Builders<MediaAsset>.Update
            .Set(asset => asset.DeletedAt, deletedAt)
            .Set(asset => asset.UpdatedAt, deletedAt)
            .Set(asset => asset.CleanupPending, true);
    }

    private static FilterDefinition<MediaAsset> BuildCleanupCompleteFilter(string id)
    {
        return Builders<MediaAsset>.Filter.And(
            Builders<MediaAsset>.Filter.Eq(asset => asset.Id, id),
            Builders<MediaAsset>.Filter.Ne(asset => asset.DeletedAt, null),
            Builders<MediaAsset>.Filter.Eq(asset => asset.CleanupPending, true));
    }

    private static UpdateDefinition<MediaAsset> BuildCleanupCompleteUpdate(DateTime updatedAt)
    {
        return Builders<MediaAsset>.Update
            .Set(asset => asset.CleanupPending, false)
            .Set(asset => asset.UpdatedAt, updatedAt);
    }

    private static FilterDefinition<MediaAsset> BuildPendingCleanupFilter()
    {
        return Builders<MediaAsset>.Filter.And(
            Builders<MediaAsset>.Filter.Eq(asset => asset.CleanupPending, true),
            Builders<MediaAsset>.Filter.Ne(asset => asset.DeletedAt, null));
    }

    private static FindOptions<MediaAsset> BuildPendingCleanupOptions(int limit)
    {
        return new FindOptions<MediaAsset>
        {
            Limit = Math.Clamp(limit, 1, ValidationLimits.MaxPageSize),
            Sort = Builders<MediaAsset>.Sort
                .Ascending(asset => asset.DeletedAt)
                .Ascending(asset => asset.Id)
        };
    }
}
