namespace HelpDeskHero.UI.Services.Realtime;

public sealed class NotificationSessionState
{
    private readonly object _sync = new();
    private long _generation;
    private string? _userId;
    private int _unreadCount;

    public event Action<int>? UnreadCountChanged;
    public event Func<Task>? ResetRequested;

    public int UnreadCount
    {
        get
        {
            lock (_sync)
                return _unreadCount;
        }
    }

    public NotificationSession BeginIdentity(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        NotificationSession session;
        Action<int>? unreadCountChanged = null;

        lock (_sync)
        {
            if (_userId != userId)
            {
                _generation++;
                _userId = userId;
                _unreadCount = 0;
                unreadCountChanged = UnreadCountChanged;
            }

            session = new NotificationSession(_generation, _userId);
        }

        unreadCountChanged?.Invoke(0);
        return session;
    }

    public NotificationSession Current
    {
        get
        {
            lock (_sync)
                return new NotificationSession(_generation, _userId);
        }
    }

    public bool IsCurrent(NotificationSession session)
    {
        lock (_sync)
            return session.Generation == _generation && session.UserId == _userId;
    }

    public bool TryUpdateUnreadCount(NotificationSession session, int unreadCount)
    {
        Action<int>? unreadCountChanged;

        lock (_sync)
        {
            if (_userId is null ||
                session.Generation != _generation ||
                session.UserId != _userId)
            {
                return false;
            }

            _unreadCount = unreadCount;
            unreadCountChanged = UnreadCountChanged;
        }

        if (IsCurrent(session))
            unreadCountChanged?.Invoke(unreadCount);

        return true;
    }

    public async Task ResetAsync()
    {
        Action<int>? unreadCountChanged;

        lock (_sync)
        {
            _generation++;
            _userId = null;
            _unreadCount = 0;
            unreadCountChanged = UnreadCountChanged;
        }

        unreadCountChanged?.Invoke(0);

        var handlers = ResetRequested;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<Task>>())
            await handler();
    }
}

public readonly record struct NotificationSession(long Generation, string? UserId);
