using FamilyPortal.Application.Common;
using FamilyPortal.Application.DTOs.Event;

namespace FamilyPortal.Application.Interfaces;

/// <summary>行事曆事件 use case 服務。</summary>
public interface IEventService
{
    /// <summary>依日期範圍取得事件。</summary>
    Task<ServiceResult<IReadOnlyList<EventResponse>>> GetByDateRangeAsync(
        EventRangeRequest request,
        CancellationToken cancellationToken);

    /// <summary>依識別碼取得事件。</summary>
    Task<ServiceResult<EventResponse>> GetByIdAsync(
        string id,
        CancellationToken cancellationToken);

    /// <summary>依事件類型取得事件。</summary>
    Task<ServiceResult<IReadOnlyList<EventResponse>>> GetByTypeAsync(
        string eventType,
        CancellationToken cancellationToken);

    /// <summary>依農曆月日取得事件。</summary>
    Task<ServiceResult<IReadOnlyList<EventResponse>>> GetByLunarDateAsync(
        int month,
        int day,
        CancellationToken cancellationToken);

    /// <summary>建立事件。</summary>
    Task<ServiceResult<EventResponse>> CreateAsync(
        UpsertEventRequest request,
        string userId,
        CancellationToken cancellationToken);

    /// <summary>更新事件。</summary>
    Task<ServiceResult<EventResponse>> UpdateAsync(
        string id,
        UpsertEventRequest request,
        string userId,
        CancellationToken cancellationToken);

    /// <summary>刪除事件。</summary>
    Task<ServiceResult<bool>> DeleteAsync(
        string id,
        CancellationToken cancellationToken);
}
