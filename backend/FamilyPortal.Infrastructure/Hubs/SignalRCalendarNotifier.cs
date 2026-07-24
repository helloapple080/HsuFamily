using FamilyPortal.Application.DTOs.Event;
using FamilyPortal.Application.Interfaces;
using Microsoft.AspNetCore.SignalR;

namespace FamilyPortal.Infrastructure.Hubs;

/// <summary>透過 SignalR 群組發布行事曆事件變更。</summary>
public sealed class SignalRCalendarNotifier(IHubContext<CalendarHub> hubContext) : ICalendarNotifier
{
    /// <inheritdoc />
    public Task EventChangedAsync(
        string eventName,
        EventResponse response,
        CancellationToken cancellationToken) =>
        hubContext.Clients.Group("FamilyCalendar")
            .SendAsync(eventName, response, cancellationToken);
}
