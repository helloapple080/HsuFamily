using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Itinerary;

namespace FamilyPortal.Application.Interfaces;

/// <summary>行程紀錄 use case 服務。</summary>
public interface IItineraryService
{
    /// <summary>以游標取得數量受限的行程紀錄。</summary>
    Task<ServiceResult<IReadOnlyList<ItineraryResponse>>> GetPagedAsync(
        PagedItineraryRequest request,
        CancellationToken cancellationToken);

    /// <summary>依識別碼取得行程紀錄。</summary>
    Task<ServiceResult<ItineraryResponse>> GetByIdAsync(
        string id,
        CancellationToken cancellationToken);

    /// <summary>搜尋數量受限的行程紀錄。</summary>
    Task<ServiceResult<IReadOnlyList<ItineraryResponse>>> SearchAsync(
        string keyword,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>取得指定成員參與且數量受限的行程紀錄。</summary>
    Task<ServiceResult<IReadOnlyList<ItineraryResponse>>> GetByParticipantAsync(
        string memberId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>建立行程紀錄。</summary>
    Task<ServiceResult<ItineraryResponse>> CreateAsync(
        UpsertItineraryRequest request,
        string userId,
        CancellationToken cancellationToken);

    /// <summary>更新行程紀錄。</summary>
    Task<ServiceResult<ItineraryResponse>> UpdateAsync(
        string id,
        UpsertItineraryRequest request,
        CancellationToken cancellationToken);

    /// <summary>刪除行程紀錄。</summary>
    Task<ServiceResult<bool>> DeleteAsync(
        string id,
        CancellationToken cancellationToken);
}
