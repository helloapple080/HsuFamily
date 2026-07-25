using FamilyPortal.Application.Models;
using DomainEvent = FamilyPortal.Domain.Entities.Event;

namespace FamilyPortal.Application.Interfaces;

/// <summary>定義農曆週期規則與曆法實作之間的轉換邊界。</summary>
public interface ILunarRecurrenceService
{
    /// <summary>
    /// 將農曆週期規則展開為指定 UTC 範圍內的純日期 occurrence。
    /// 結果的國曆日期一律為 UTC 午夜，不含原事件時間或期間。
    /// </summary>
    /// <param name="rule">包含農曆週期設定的來源事件。</param>
    /// <param name="rangeStart">含首的 UTC 查詢開始。</param>
    /// <param name="rangeEnd">含尾的 UTC 查詢結束。</param>
    /// <param name="cancellationToken">取消作業的權杖。</param>
    /// <returns>依國曆日期與穩定識別碼排序的 occurrence 清單。</returns>
    /// <exception cref="ArgumentException">查詢範圍或規則生效起訖不是 UTC，或查詢範圍反轉。</exception>
    Task<IReadOnlyList<LunarOccurrence>> ExpandAsync(
        DomainEvent rule,
        DateTime rangeStart,
        DateTime rangeEnd,
        CancellationToken cancellationToken);
}
