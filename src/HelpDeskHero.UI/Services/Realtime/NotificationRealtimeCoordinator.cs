using HelpDeskHero.Shared.Contracts.Notifications;
using HelpDeskHero.UI.Services.Api;

namespace HelpDeskHero.UI.Services.Realtime;

/// <summary>Maintains the server-backed notification state for the current UI session.</summary>
public sealed class NotificationRealtimeCoordinator : IAsyncDisposable
{
    private readonly INotificationRealtimeClient _realtime;
    private readonly NotificationApiClient _notificationApi;
    private readonly NotificationSessionState _sessionState;
    private readonly ILogger<NotificationRealtimeCoordinator> _logger;
    private readonly HashSet<int> _receivedNotificationIds = [];
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private Func<UserNotificationDto, Task>? _notificationHandler;
    private bool _started;
    private bool _disposed;

    public NotificationRealtimeCoordinator(
        INotificationRealtimeClient realtime,
        NotificationApiClient notificationApi,
        NotificationSessionState sessionState,
        ILogger<NotificationRealtimeCoordinator> logger)
    {
        _realtime = realtime;
        _notificationApi = notificationApi;
        _sessionState = sessionState;
        _logger = logger;
        _sessionState.ResetRequested += ResetRealtimeAsync;
    }

    public event Func<IReadOnlyList<UserNotificationDto>, Task>? NotificationsChanged;
    public event Func<UserNotificationDto, Task>? NotificationReceived;

    public async Task StartAsync(string userId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        await _lifecycleLock.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_started && _sessionState.Current.UserId == userId)
                return;

            var session = _sessionState.BeginIdentity(userId);
            await StopRealtimeCoreAsync(ct);

            _notificationHandler = notification =>
                HandleNotificationCreatedSafelyAsync(notification, session);
            _realtime.OnNotificationCreated += _notificationHandler;

            try
            {
                await _realtime.StartAsync(ct);
                _started = true;
                await RefreshCoreAsync(session, ct);
            }
            catch
            {
                await StopRealtimeCoreAsync(CancellationToken.None);
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task ResetAsync()
    {
        await _sessionState.ResetAsync();
    }

    public async Task<IReadOnlyList<UserNotificationDto>> RefreshAsync(CancellationToken ct = default)
    {
        return await RefreshCoreAsync(_sessionState.Current, ct);
    }

    private async Task<IReadOnlyList<UserNotificationDto>> RefreshCoreAsync(
        NotificationSession session,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(session.UserId) || !_sessionState.IsCurrent(session))
            return [];

        var notifications = await _notificationApi.GetMineAsync(ct);

        if (!_sessionState.TryUpdateUnreadCount(
                session,
                notifications.Count(x => !x.IsRead)))
        {
            return [];
        }

        await InvokeNotificationsChangedAsync(notifications, session);
        return notifications;
    }

    private async Task HandleNotificationCreatedSafelyAsync(
        UserNotificationDto notification,
        NotificationSession session)
    {
        try
        {
            if (!_sessionState.IsCurrent(session))
                return;

            lock (_receivedNotificationIds)
            {
                if (!_receivedNotificationIds.Add(notification.Id))
                    return;
            }

            await RefreshCoreAsync(session, CancellationToken.None);

            if (!_sessionState.IsCurrent(session))
                return;

            var handlers = NotificationReceived;
            if (handlers is null)
                return;

            foreach (var handler in handlers.GetInvocationList().Cast<Func<UserNotificationDto, Task>>())
            {
                if (!_sessionState.IsCurrent(session))
                    return;

                await handler(notification);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to process a realtime notification.");
        }
    }

    private async Task InvokeNotificationsChangedAsync(
        IReadOnlyList<UserNotificationDto> notifications,
        NotificationSession session)
    {
        var handlers = NotificationsChanged;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList()
                     .Cast<Func<IReadOnlyList<UserNotificationDto>, Task>>())
        {
            if (!_sessionState.IsCurrent(session))
                return;

            await handler(notifications);
        }
    }

    private async Task ResetRealtimeAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            await StopRealtimeCoreAsync(CancellationToken.None);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    private async Task StopRealtimeCoreAsync(CancellationToken ct)
    {
        if (_notificationHandler is not null)
        {
            _realtime.OnNotificationCreated -= _notificationHandler;
            _notificationHandler = null;
        }

        _started = false;

        lock (_receivedNotificationIds)
            _receivedNotificationIds.Clear();

        await _realtime.StopAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        _sessionState.ResetRequested -= ResetRealtimeAsync;

        await _lifecycleLock.WaitAsync();
        try
        {
            await StopRealtimeCoreAsync(CancellationToken.None);
        }
        finally
        {
            _lifecycleLock.Release();
            _lifecycleLock.Dispose();
        }
    }
}
