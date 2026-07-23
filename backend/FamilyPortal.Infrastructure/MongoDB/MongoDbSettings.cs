namespace FamilyPortal.Infrastructure.MongoDB;

/// <summary>
/// MongoDB 連線設定 — 對應 appsettings.json 中的 MongoDbSettings 區塊
/// </summary>
public class MongoDbSettings
{
    /// <summary>MongoDB 連線字串</summary>
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";

    /// <summary>資料庫名稱</summary>
    public string DatabaseName { get; set; } = "FamilyPortal";
}
