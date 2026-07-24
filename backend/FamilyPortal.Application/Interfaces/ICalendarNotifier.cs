using FamilyPortal.Application.DTOs.Event;

namespace FamilyPortal.Application.Interfaces;

/// <summary>行事曆事件變更通知埠。</summary>
public interface ICalendarNotifier
{
    /// <summary>通知已驗證的行事曆連線事件資料已變更。</summary>
    /// <param name="eventName">SignalR 事件名稱。</param>
    /// <param name="response">變更後或刪除前的事件資料。</param>
    /// <param name="cancellationToken">取消權杖。</param>
    Task EventChangedAsync(
        string eventName,
        EventResponse response,
        CancellationToken cancellationToken);
}
