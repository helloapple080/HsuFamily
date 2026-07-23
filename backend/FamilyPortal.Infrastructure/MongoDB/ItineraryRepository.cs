using FamilyPortal.Application.Interfaces;
using FamilyPortal.Domain.Entities;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FamilyPortal.Infrastructure.MongoDB;

/// <summary>
/// 行程紀錄 Repository 實作 — 包含游標式分頁與全文搜尋
/// </summary>
public class ItineraryRepository : MongoRepository<Itinerary>, IItineraryRepository
{
    public ItineraryRepository(MongoDbContext context) : base(context, "Itineraries") { }

    /// <inheritdoc />
    public async Task<List<Itinerary>> GetPagedAsync(
        string? tag = null,
        DateTime? cursorDate = null,
        string? cursorId = null,
        int pageSize = 20)
    {
        var filterBuilder = Builders<Itinerary>.Filter;
        var filters = new List<FilterDefinition<Itinerary>>();

        // 標籤篩選
        if (!string.IsNullOrWhiteSpace(tag))
        {
            filters.Add(filterBuilder.AnyEq(i => i.Tags, tag));
        }

        // 游標分頁：取得「比游標更早」的資料
        if (cursorDate.HasValue && !string.IsNullOrWhiteSpace(cursorId))
        {
            var cursorFilter = filterBuilder.Or(
                filterBuilder.Lt(i => i.Date, cursorDate.Value),
                filterBuilder.And(
                    filterBuilder.Eq(i => i.Date, cursorDate.Value),
                    filterBuilder.Lt(i => i.Id, cursorId)
                )
            );
            filters.Add(cursorFilter);
        }

        var combinedFilter = filters.Count > 0
            ? filterBuilder.And(filters)
            : filterBuilder.Empty;

        return await _collection
            .Find(combinedFilter)
            .SortByDescending(i => i.Date)
            .ThenByDescending(i => i.Id)
            .Limit(pageSize)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<Itinerary>> GetByParticipantAsync(string memberId)
    {
        var filter = Builders<Itinerary>.Filter
            .ElemMatch(i => i.Participants, p => p.MemberId == memberId);

        return await _collection
            .Find(filter)
            .SortByDescending(i => i.Date)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<List<Itinerary>> SearchAsync(string keyword, int limit = 20)
    {
        var filter = Builders<Itinerary>.Filter.Text(keyword);
        return await _collection
            .Find(filter)
            .Limit(limit)
            .ToListAsync();
    }
}
