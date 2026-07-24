using FamilyPortal.Domain.Entities;

namespace FamilyPortal.Application.Interfaces;

/// <summary>
/// 行事曆事件 Repository 介面 — 擴充 Event 專屬查詢
/// </summary>
public interface IEventRepository : IRepository<Event>
{
    /// <summary>根據日期範圍取得事件（月檢視查詢）</summary>
    Task<List<Event>> GetByDateRangeAsync(
        DateTime start,
        DateTime end,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>根據事件類型取得事件</summary>
    Task<List<Event>> GetByEventTypeAsync(
        Domain.Enums.EventType eventType,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>根據標籤取得事件</summary>
    Task<List<Event>> GetByTagAsync(
        string tag,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>根據農曆月日取得事件（用於農曆提醒）</summary>
    Task<List<Event>> GetByLunarDateAsync(
        int month,
        int day,
        int limit,
        CancellationToken cancellationToken = default);
}
