using HelpDeskHero.Shared.Contracts.Notifications;
using HelpDeskHero.UI.Services.Api;

namespace HelpDeskHero.UI.Services.Realtime;

/// <summary>Maintains the server-backed notification state for the current UI session.</summary>
public sealed class NotificationRealtimeCoordinator
{
    private readonly INotificationRealtimeClient _realtime;
    private readonly NotificationApiClient _notificationApi;
    private readonly HashSet<int> _receivedNotificationIds = [];
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private bool _started;

    public NotificationRealtimeCoordinator(
        INotificationRealtimeClient realtime,
        NotificationApiClient notificationApi)
    {
        _realtime = realtime;
        _notificationApi = notificationApi;
    }

    public event Func<IReadOnlyList<UserNotificationDto>, Task>? NotificationsChanged;
    public event Func<UserNotificationDto, Task>? NotificationReceived;

    public async Task StartAsync(CancellationToken ct = default)
    {
        await _startLock.WaitAsync(ct);
        try
        {
            if (_started)
                return;

            _started = true;
            _realtime.OnNotificationCreated += HandleNotificationCreatedAsync;
            await _realtime.StartAsync(ct);
            await RefreshAsync(ct);
        }
        catch
        {
            _started = false;
            _realtime.OnNotificationCreated -= HandleNotificationCreatedAsync;
            throw;
        }
        finally
        {
            _startLock.Release();
        }
    }

    public async Task<IReadOnlyList<UserNotificationDto>> RefreshAsync(CancellationToken ct = default)
    {
        var notifications = await _notificationApi.GetMineAndUpdateUnreadCountAsync(ct);

        if (NotificationsChanged is not null)
            await NotificationsChanged(notifications);

        return notifications;
    }

    private async Task HandleNotificationCreatedAsync(UserNotificationDto notification)
    {
        lock (_receivedNotificationIds)
        {
            if (!_receivedNotificationIds.Add(notification.Id))
                return;
        }

        // The SignalR payload is a prompt; the API remains the source of truth for counts and lists.
        await RefreshAsync();

        if (NotificationReceived is not null)
            await NotificationReceived(notification);
    }
}
