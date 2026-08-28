using System.Net.Http.Json;
using TixeNex.Shared.Contracts.Notifications;
using TixeNex.UI.Services.Realtime;

namespace TixeNex.UI.Services.Api;

public sealed class NotificationApiClient
{
    private readonly HttpClient _http;
    private readonly NotificationSessionState _sessionState;

    public NotificationApiClient(HttpClient http, NotificationSessionState sessionState)
    {
        _http = http;
        _sessionState = sessionState;
    }

    public event Action<int>? UnreadCountChanged
    {
        add => _sessionState.UnreadCountChanged += value;
        remove => _sessionState.UnreadCountChanged -= value;
    }

    public int CurrentUnreadCount => _sessionState.UnreadCount;
    public NotificationSession CurrentSession => _sessionState.Current;

    public async Task<IReadOnlyList<UserNotificationDto>> GetMineAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<UserNotificationDto>>(
            "api/notifications/mine",
            ct) ?? [];
    }

    public async Task<IReadOnlyList<UserNotificationDto>> GetMineAndUpdateUnreadCountAsync(CancellationToken ct = default)
    {
        var session = _sessionState.Current;
        var notifications = await GetMineAsync(ct);
        _sessionState.TryUpdateUnreadCount(session, notifications.Count(x => !x.IsRead));
        return notifications;
    }

    public async Task<HttpResponseMessage> MarkAsReadAsync(int id, CancellationToken ct = default)
    {
        return await _http.PostAsync($"api/notifications/{id}/read", content: null, ct);
    }

    public async Task<HttpResponseMessage?> MarkAllAsReadAsync(
        IEnumerable<UserNotificationDto> notifications,
        CancellationToken ct = default)
    {
        var unreadIds = notifications
            .Where(x => !x.IsRead)
            .Select(x => x.Id)
            .Distinct()
            .ToArray();

        foreach (var id in unreadIds)
        {
            var response = await MarkAsReadAsync(id, ct);
            if (!response.IsSuccessStatusCode)
            {
                return response;
            }
        }

        return null;
    }

    public void UpdateUnreadCount(
        IEnumerable<UserNotificationDto> notifications,
        NotificationSession? session = null)
    {
        _sessionState.TryUpdateUnreadCount(
            session ?? _sessionState.Current,
            notifications.Count(x => !x.IsRead));
    }
}
