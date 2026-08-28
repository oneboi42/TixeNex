using TixeNex.Api.Domain;
using TixeNex.Api.Hubs;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Shared.Contracts.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;

namespace TixeNex.Api.Infrastructure.Notifications;

public sealed class InAppNotificationSender : INotificationSender
{
    private readonly AppDbContext _db;
    private readonly IHubContext<TicketsHub> _hubContext;

    public InAppNotificationSender(AppDbContext db, IHubContext<TicketsHub> hubContext)
    {
        _db = db;
        _hubContext = hubContext;
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

        await _hubContext.Clients.Group(TicketsHub.UserGroup(message.UserId))
            .SendAsync("NotificationCreated", new UserNotificationDto
            {
                Id = entity.Id,
                Subject = entity.Subject,
                Body = entity.Body,
                IsRead = entity.IsRead,
                CreatedAtUtc = entity.CreatedAtUtc
            }, ct);
    }
}
