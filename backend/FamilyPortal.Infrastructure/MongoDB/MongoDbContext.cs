using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace FamilyPortal.Infrastructure.MongoDB;

/// <summary>
/// MongoDB 連線工廠 — 管理 MongoClient 生命週期（Singleton）
/// MongoClient 本身是執行緒安全的，適合單例使用
/// </summary>
public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IOptions<MongoDbSettings> settings)
    {
        var client = new MongoClient(settings.Value.ConnectionString);
        _database = client.GetDatabase(settings.Value.DatabaseName);
    }

    /// <summary>取得指定名稱的 Collection</summary>
    public IMongoCollection<T> GetCollection<T>(string collectionName)
        => _database.GetCollection<T>(collectionName);

    /// <summary>取得底層 IMongoDatabase（供進階操作使用）</summary>
    public IMongoDatabase Database => _database;
}
