using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;

namespace FamilyPortal.Application.Interfaces;

/// <summary>媒體中繼資料的持久化邊界。</summary>
public interface IMediaRepository
{
    /// <summary>根據 ID 取得未刪除的媒體。</summary>
    Task<MediaAsset?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>計算指定擁有者與用途的未刪除媒體數量。</summary>
    Task<long> CountActiveByOwnerAndPurposeAsync(
        string ownerEntityId,
        MediaPurpose purpose,
        CancellationToken cancellationToken = default);

    /// <summary>建立媒體中繼資料。</summary>
    Task CreateAsync(
        MediaAsset asset,
        CancellationToken cancellationToken = default);

    /// <summary>將未刪除媒體標記為待清理。</summary>
    /// <returns>有更新到未刪除媒體時為 true。</returns>
    Task<bool> SoftDeleteAsync(
        string id,
        CancellationToken cancellationToken = default);

    /// <summary>取得有上限且順序穩定的待清理媒體。</summary>
    Task<List<MediaAsset>> GetPendingCleanupAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
