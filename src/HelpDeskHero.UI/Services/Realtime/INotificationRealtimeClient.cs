using HelpDeskHero.Shared.Contracts.Notifications;

namespace HelpDeskHero.UI.Services.Realtime;

public interface INotificationRealtimeClient
{
    event Func<UserNotificationDto, Task>? OnNotificationCreated;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
