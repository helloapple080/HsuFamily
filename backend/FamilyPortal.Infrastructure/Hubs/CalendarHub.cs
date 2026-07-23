using Microsoft.AspNetCore.SignalR;

namespace FamilyPortal.Infrastructure.Hubs;

/// <summary>
/// 行事曆 SignalR Hub — 即時推播行事曆事件更新
/// 
/// 當 Admin 新增、修改或刪除事件時，透過此 Hub 廣播給所有在線用戶
/// 前端收到通知後可即時更新行事曆畫面，無需手動重整
/// </summary>
public class CalendarHub : Hub
{
    /// <summary>Hub 路由路徑</summary>
    public const string HubUrl = "/hubs/calendar";

    /// <summary>
    /// 用戶連線時觸發
    /// 可記錄在線用戶或加入群組
    /// </summary>
    public override async Task OnConnectedAsync()
    {
        // 將用戶加入 "FamilyCalendar" 群組
        await Groups.AddToGroupAsync(Context.ConnectionId, "FamilyCalendar");
        await base.OnConnectedAsync();
    }

    /// <summary>
    /// 用戶斷線時觸發
    /// </summary>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, "FamilyCalendar");
        await base.OnDisconnectedAsync(exception);
    }
}
