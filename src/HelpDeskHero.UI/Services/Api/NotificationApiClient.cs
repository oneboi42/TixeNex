using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Notifications;

namespace HelpDeskHero.UI.Services.Api;

public sealed class NotificationApiClient
{
    private readonly HttpClient _http;

    public NotificationApiClient(HttpClient http)
    {
        _http = http;
    }

    public event Action<int>? UnreadCountChanged;

    public async Task<IReadOnlyList<UserNotificationDto>> GetMineAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<UserNotificationDto>>(
            "api/notifications/mine",
            ct) ?? [];
    }

    public async Task<IReadOnlyList<UserNotificationDto>> GetMineAndUpdateUnreadCountAsync(CancellationToken ct = default)
    {
        var notifications = await GetMineAsync(ct);
        UpdateUnreadCount(notifications);
        return notifications;
    }

    public async Task<HttpResponseMessage> MarkAsReadAsync(int id, CancellationToken ct = default)
    {
        return await _http.PostAsync($"api/notifications/{id}/read", content: null, ct);
    }

    public void UpdateUnreadCount(IEnumerable<UserNotificationDto> notifications)
    {
        UnreadCountChanged?.Invoke(notifications.Count(x => !x.IsRead));
    }
}
