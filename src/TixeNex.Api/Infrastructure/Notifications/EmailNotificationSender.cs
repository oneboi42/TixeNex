namespace TixeNex.Api.Infrastructure.Notifications;

public sealed class EmailNotificationSender : INotificationSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
