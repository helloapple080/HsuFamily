using FamilyPortal.Application.Common;
using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using MongoDB.Driver;

namespace FamilyPortal.Infrastructure.MongoDB;

/// <summary>
/// 家庭成員 Repository 實作
/// </summary>
public class MemberRepository : MongoRepository<Member>, IMemberRepository
{
    public MemberRepository(MongoDbContext context) : base(context, "Members") { }

    /// <inheritdoc />
    public async Task<List<Member>> GetActiveOrderedAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(m => m.IsActive)
            .SortBy(m => m.SortOrder)
            .Limit(Math.Clamp(limit, 1, ValidationLimits.MaxPageSize))
            .ToListAsync(cancellationToken);
    }
}
