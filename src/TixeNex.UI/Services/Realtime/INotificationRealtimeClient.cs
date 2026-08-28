using TixeNex.Shared.Contracts.Notifications;

namespace TixeNex.UI.Services.Realtime;

public interface INotificationRealtimeClient
{
    event Func<UserNotificationDto, Task>? OnNotificationCreated;

    Task StartAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}
