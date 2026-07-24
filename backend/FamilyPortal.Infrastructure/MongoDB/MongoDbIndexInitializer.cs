using FamilyPortal.Domain.Entities;
using MongoDB.Driver;

namespace FamilyPortal.Infrastructure.MongoDB;

/// <summary>
/// MongoDB 索引初始化器 — 在應用程式啟動時建立所有必要的索引
/// 確保查詢效能與資料完整性
/// </summary>
public class MongoDbIndexInitializer
{
    private readonly MongoDbContext _context;

    public MongoDbIndexInitializer(MongoDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// 建立所有 Collection 的索引
    /// 此方法在 Program.cs 應用程式啟動時呼叫
    /// </summary>
    public async Task InitializeAsync()
    {
        await CreateUserIndexesAsync();
        await CreateMemberIndexesAsync();
        await CreateEventIndexesAsync();
        await CreateItineraryIndexesAsync();
    }

    private async Task CreateUserIndexesAsync()
    {
        var collection = _context.GetCollection<User>("Users");
        var indexModels = new List<CreateIndexModel<User>>
        {
            // 唯一索引：帳號不重複
            new(
                Builders<User>.IndexKeys.Ascending(u => u.Username),
                new CreateIndexOptions { Unique = true, Name = "idx_username_unique" }
            ),
            // 唯一索引：信箱不重複
            new(
                Builders<User>.IndexKeys.Ascending(u => u.Email),
                new CreateIndexOptions { Unique = true, Name = "idx_email_unique" }
            ),
            // Refresh Token 雜湊查詢索引（資料庫不保存原始 Token）
            new(
                Builders<User>.IndexKeys.Ascending("refreshTokens.tokenHash"),
                new CreateIndexOptions { Sparse = true, Name = "idx_refresh_token_hash" }
            )
        };

        await collection.Indexes.CreateManyAsync(indexModels);
    }

    private async Task CreateMemberIndexesAsync()
    {
        var collection = _context.GetCollection<Member>("Members");
        var indexModels = new List<CreateIndexModel<Member>>
        {
            // 排序查詢：啟用狀態 + 排序順序
            new(
                Builders<Member>.IndexKeys
                    .Ascending(m => m.IsActive)
                    .Ascending(m => m.SortOrder),
                new CreateIndexOptions { Name = "idx_active_sort" }
            ),
            // 姓名搜尋
            new(
                Builders<Member>.IndexKeys.Ascending(m => m.Name),
                new CreateIndexOptions { Name = "idx_name" }
            )
        };

        await collection.Indexes.CreateManyAsync(indexModels);
    }

    private async Task CreateEventIndexesAsync()
    {
        var collection = _context.GetCollection<Event>("Events");
        var indexModels = new List<CreateIndexModel<Event>>
        {
            // 複合索引：日期範圍 + 類型（行事曆月檢視主力查詢）
            new(
                Builders<Event>.IndexKeys
                    .Ascending(e => e.StartDate)
                    .Ascending(e => e.EventType),
                new CreateIndexOptions { Name = "idx_start_date_type" }
            ),
            // 跨日事件重疊查詢：StartDate <= rangeEnd && EndDate >= rangeStart
            new(
                Builders<Event>.IndexKeys
                    .Ascending(e => e.StartDate)
                    .Ascending(e => e.EndDate),
                new CreateIndexOptions { Name = "idx_start_end_date" }
            ),
            // 建立者索引
            new(
                Builders<Event>.IndexKeys.Ascending(e => e.CreatedBy),
                new CreateIndexOptions { Name = "idx_created_by" }
            ),
            // 標籤多值索引
            new(
                Builders<Event>.IndexKeys.Ascending(e => e.Tags),
                new CreateIndexOptions { Name = "idx_tags" }
            ),
            // 農曆日期查詢索引
            new(
                Builders<Event>.IndexKeys
                    .Ascending(e => e.IsLunarDate)
                    .Ascending("lunarDate.month")
                    .Ascending("lunarDate.day"),
                new CreateIndexOptions { Name = "idx_lunar_date" }
            ),
            // 事件類型 + 日期複合索引
            new(
                Builders<Event>.IndexKeys
                    .Ascending(e => e.EventType)
                    .Descending(e => e.StartDate),
                new CreateIndexOptions { Name = "idx_type_date_desc" }
            )
        };

        await collection.Indexes.CreateManyAsync(indexModels);
    }

    private async Task CreateItineraryIndexesAsync()
    {
        var collection = _context.GetCollection<Itinerary>("Itineraries");

        // 先建立一般索引
        var indexModels = new List<CreateIndexModel<Itinerary>>
        {
            // 日期降序 + 標籤（歷史查詢主力，支援游標分頁）
            new(
                Builders<Itinerary>.IndexKeys
                    .Descending(i => i.Date)
                    .Ascending(i => i.Tags),
                new CreateIndexOptions { Name = "idx_date_desc_tags" }
            ),
            // 游標分頁排序：Date + _id 必須與查詢排序一致
            new(
                Builders<Itinerary>.IndexKeys
                    .Descending(i => i.Date)
                    .Descending(i => i.Id),
                new CreateIndexOptions { Name = "idx_date_desc_id_desc" }
            ),
            // 標籤獨立索引
            new(
                Builders<Itinerary>.IndexKeys.Ascending(i => i.Tags),
                new CreateIndexOptions { Name = "idx_tags" }
            ),
            // 標籤篩選 + 穩定日期游標排序（保留既有索引，待 staging 證據再評估清理）
            new(
                Builders<Itinerary>.IndexKeys
                    .Ascending(i => i.Tags)
                    .Descending(i => i.Date)
                    .Descending(i => i.Id),
                new CreateIndexOptions { Name = "idx_tags_date_id" }
            ),
            // 建立者索引
            new(
                Builders<Itinerary>.IndexKeys.Ascending(i => i.CreatedBy),
                new CreateIndexOptions { Name = "idx_created_by" }
            ),
            // 參與成員 + 日期複合索引
            new(
                Builders<Itinerary>.IndexKeys
                    .Ascending("participants.memberId")
                    .Descending(i => i.Date),
                new CreateIndexOptions { Name = "idx_participant_date" }
            ),
            // 全文索引：標題與描述
            new(
                Builders<Itinerary>.IndexKeys
                    .Text(i => i.Title)
                    .Text(i => i.Description),
                new CreateIndexOptions { Name = "idx_text_search" }
            )
        };

        await collection.Indexes.CreateManyAsync(indexModels);
    }
}
