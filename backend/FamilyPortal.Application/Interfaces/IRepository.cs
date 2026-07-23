using FamilyPortal.Domain.Entities;
using System.Linq.Expressions;

namespace FamilyPortal.Application.Interfaces;

/// <summary>
/// 泛型 Repository 介面 — 所有 Collection 共用的 CRUD 操作
/// 遵循 Repository Pattern，隔離資料存取邏輯
/// </summary>
/// <typeparam name="T">繼承自 BaseEntity 的實體型別</typeparam>
public interface IRepository<T> where T : BaseEntity
{
    /// <summary>根據 ID 取得單一文件</summary>
    Task<T?> GetByIdAsync(string id);

    /// <summary>取得所有文件</summary>
    Task<List<T>> GetAllAsync();

    /// <summary>根據篩選條件取得文件清單</summary>
    Task<List<T>> FindAsync(Expression<Func<T, bool>> filter);

    /// <summary>根據篩選條件取得單一文件</summary>
    Task<T?> FindOneAsync(Expression<Func<T, bool>> filter);

    /// <summary>新增一筆文件</summary>
    Task CreateAsync(T entity);

    /// <summary>更新一筆文件（整筆替換）</summary>
    Task UpdateAsync(string id, T entity);

    /// <summary>刪除一筆文件</summary>
    Task DeleteAsync(string id);

    /// <summary>檢查文件是否存在</summary>
    Task<bool> ExistsAsync(Expression<Func<T, bool>> filter);

    /// <summary>取得符合條件的文件總數</summary>
    Task<long> CountAsync(Expression<Func<T, bool>>? filter = null);
}
