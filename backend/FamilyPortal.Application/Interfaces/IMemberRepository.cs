using FamilyPortal.Domain.Entities;

namespace FamilyPortal.Application.Interfaces;

/// <summary>
/// 家庭成員 Repository 介面
/// </summary>
public interface IMemberRepository : IRepository<Member>
{
    /// <summary>取得依排序順序排列且數量受限的啟用成員。</summary>
    /// <param name="limit">最多回傳的成員數量。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    /// <returns>啟用成員清單。</returns>
    Task<List<Member>> GetActiveOrderedAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
