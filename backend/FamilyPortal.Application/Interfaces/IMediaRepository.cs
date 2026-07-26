using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using FamilyPortal.Application.Models;

namespace FamilyPortal.Application.Interfaces;

/// <summary>媒體中繼資料的持久化邊界。</summary>
public interface IMediaRepository
{
    /// <summary>根據 ID 取得未刪除的媒體。</summary>
    Task<MediaAsset?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>原子建立媒體中繼資料，並回報 active slot 衝突。</summary>
    Task<MediaCreateResult> TryCreateAsync(
        MediaAsset asset,
        CancellationToken cancellationToken = default);

    /// <summary>原子更新未刪除媒體的可見性，並回傳更新後資料。</summary>
    Task<MediaAsset?> UpdateVisibilityAsync(
        string id,
        MediaVisibility visibility,
        CancellationToken cancellationToken = default);

    /// <summary>將未刪除媒體標記為待清理。</summary>
    /// <returns>有更新到未刪除媒體時為 true。</returns>
    Task<bool> SoftDeleteAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>將已軟刪除媒體的儲存清理狀態標記為完成。</summary>
    Task MarkCleanupCompleteAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>取得有上限且順序穩定的待清理媒體。</summary>
    Task<List<MediaAsset>> GetPendingCleanupAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
