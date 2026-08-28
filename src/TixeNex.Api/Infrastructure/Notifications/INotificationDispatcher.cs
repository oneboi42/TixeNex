namespace TixeNex.Api.Infrastructure.Notifications;

public interface INotificationDispatcher
{
    Task DispatchAsync(NotificationMessage message, CancellationToken ct = default);
}
