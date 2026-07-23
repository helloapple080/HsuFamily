using FamilyPortal.Domain.Entities;

namespace FamilyPortal.Application.Interfaces;

/// <summary>
/// 行程紀錄 Repository 介面 — 支援分頁與標籤篩選
/// </summary>
public interface IItineraryRepository : IRepository<Itinerary>
{
    /// <summary>
    /// 游標式分頁查詢 — 基於日期與 ID 的高效分頁
    /// </summary>
    /// <param name="tag">篩選標籤（可選）</param>
    /// <param name="cursorDate">游標日期（上一頁最後一筆的日期）</param>
    /// <param name="cursorId">游標 ID（上一頁最後一筆的 ID）</param>
    /// <param name="pageSize">每頁筆數</param>
    /// <returns>分頁結果</returns>
    Task<List<Itinerary>> GetPagedAsync(
        string? tag = null,
        DateTime? cursorDate = null,
        string? cursorId = null,
        int pageSize = 20);

    /// <summary>取得某位成員參與過的所有行程</summary>
    Task<List<Itinerary>> GetByParticipantAsync(string memberId);

    /// <summary>全文搜尋行程（標題與描述）</summary>
    Task<List<Itinerary>> SearchAsync(string keyword, int limit = 20);
}
