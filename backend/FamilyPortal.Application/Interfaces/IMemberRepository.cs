using FamilyPortal.Domain.Entities;

namespace FamilyPortal.Application.Interfaces;

/// <summary>
/// 家庭成員 Repository 介面
/// </summary>
public interface IMemberRepository : IRepository<Member>
{
    /// <summary>取得所有啟用的成員（依排序順序）</summary>
    Task<List<Member>> GetActiveOrderedAsync();
}
