using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Infrastructure.Notifications;

public sealed class InAppNotificationSender : INotificationSender
{
    private readonly AppDbContext _db;

    public InAppNotificationSender(AppDbContext db)
    {
        _db = db;
    }

    public NotificationChannel Channel => NotificationChannel.InApp;

    public async Task SendAsync(NotificationMessage message, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(message.UserId))
            throw new InvalidOperationException("In-app notification requires a user id.");

        var userExists = await _db.Users
            .AsNoTracking()
            .AnyAsync(x => x.Id == message.UserId, ct);

        if (!userExists)
            throw new InvalidOperationException("In-app notification user id does not exist.");

        var entity = new UserNotification
        {
            UserId = message.UserId,
            Subject = message.Subject,
            Body = message.Body,
            CreatedAtUtc = DateTime.UtcNow,
            IsRead = false
        };

        _db.UserNotifications.Add(entity);
        await _db.SaveChangesAsync(ct);
    }
}
