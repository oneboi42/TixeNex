using System.Net.Http.Json;

namespace TixeNex.Api.Infrastructure.Notifications;

public sealed class WebhookNotificationSender : INotificationSender
{
    private readonly HttpClient _http;
    private readonly IConfiguration _configuration;

    public WebhookNotificationSender(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _configuration = configuration;
    }

    public NotificationChannel Channel => NotificationChannel.Webhook;

    public async Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        var webhookUrl = _configuration["Notifications:WebhookUrl"];
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return;

        var payload = new
        {
            text = $"{message.Subject}: {message.Body}",
            userId = message.UserId,
            channel = message.Channel.ToString()
        };

        await _http.PostAsJsonAsync(webhookUrl, payload, ct);
    }
}
