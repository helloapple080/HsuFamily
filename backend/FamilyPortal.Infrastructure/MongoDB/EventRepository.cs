using FamilyPortal.Application.Interfaces;
using FamilyPortal.Application.Common;
using FamilyPortal.Domain.Entities;
using FamilyPortal.Domain.Enums;
using MongoDB.Driver;

namespace FamilyPortal.Infrastructure.MongoDB;

/// <summary>
/// 行事曆事件 Repository 實作 — 支援日期範圍、類型、農曆日期查詢
/// </summary>
public class EventRepository : MongoRepository<Event>, IEventRepository
{
    public EventRepository(MongoDbContext context) : base(context, "Events") { }

    /// <inheritdoc />
    public async Task<List<Event>> GetByDateRangeAsync(
        DateTime start,
        DateTime end,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // 重疊查詢：事件開始在查詢結束前，且事件結束在查詢開始後。
        // 無 EndDate 的事件以 StartDate 作為單日事件處理。
        var filter = Builders<Event>.Filter.And(
            Builders<Event>.Filter.Lte(e => e.StartDate, end),
            Builders<Event>.Filter.Or(
                Builders<Event>.Filter.And(
                    Builders<Event>.Filter.Eq(e => e.EndDate, null),
                    Builders<Event>.Filter.Gte(e => e.StartDate, start)
                ),
                Builders<Event>.Filter.Gte(e => e.EndDate, start)
            )
        );

        return await _collection
            .Find(filter)
            .SortBy(e => e.StartDate)
            .ThenBy(e => e.Id)
            .Limit(Math.Clamp(limit, 1, ValidationLimits.MaxPageSize))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<Event>> GetByEventTypeAsync(
        EventType eventType,
        int limit,
        CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(e => e.EventType == eventType)
            .SortByDescending(e => e.StartDate)
            .ThenBy(e => e.Id)
            .Limit(Math.Clamp(limit, 1, ValidationLimits.MaxPageSize))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<Event>> GetByTagAsync(
        string tag,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<Event>.Filter.AnyEq(e => e.Tags, tag);
        return await _collection
            .Find(filter)
            .SortByDescending(e => e.StartDate)
            .ThenBy(e => e.Id)
            .Limit(Math.Clamp(limit, 1, ValidationLimits.MaxPageSize))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<Event>> GetByLunarDateAsync(
        int month,
        int day,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<Event>.Filter.And(
            Builders<Event>.Filter.Eq(e => e.IsLunarDate, true),
            Builders<Event>.Filter.Eq("lunarDate.month", month),
            Builders<Event>.Filter.Eq("lunarDate.day", day)
        );

        return await _collection
            .Find(filter)
            .SortBy(e => e.StartDate)
            .ThenBy(e => e.Id)
            .Limit(Math.Clamp(limit, 1, ValidationLimits.MaxPageSize))
            .ToListAsync(cancellationToken);
    }
}
