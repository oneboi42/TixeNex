using HelpDeskHero.Api.BackgroundJobs.Contracts;
using HelpDeskHero.Api.Infrastructure.Notifications;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.BackgroundJobs;

public sealed class NotificationJob : INotificationJob
{
    private readonly AppDbContext _db;
    private readonly INotificationDispatcher _dispatcher;

    public NotificationJob(AppDbContext db, INotificationDispatcher dispatcher)
    {
        _db = db;
        _dispatcher = dispatcher;
    }

    public async Task SendTicketCreatedNotificationsAsync(int ticketId, CancellationToken ct = default)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == ticketId, ct);

        if (ticket is null)
            return;

        var recipientUserIds = await GetUserIdsInRolesAsync(["Admin", "Agent"], ct);

        foreach (var userId in recipientUserIds)
        {
            await _dispatcher.DispatchAsync(new NotificationMessage
            {
                Channel = NotificationChannel.InApp,
                Subject = $"Nowe zgloszenie: {ticket.Number}",
                Body = $"Utworzono zgloszenie {ticket.Number} - {ticket.Title}",
                UserId = userId
            }, ct);
        }
    }

    public async Task SendDailySummaryAsync(CancellationToken ct = default)
    {
        var openCount = await _db.Tickets
            .AsNoTracking()
            .CountAsync(x => !x.IsDeleted && x.Status != "Closed", ct);

        var recipientUserIds = await GetUserIdsInRolesAsync(["Admin"], ct);

        foreach (var userId in recipientUserIds)
        {
            await _dispatcher.DispatchAsync(new NotificationMessage
            {
                Channel = NotificationChannel.InApp,
                Subject = "HelpDeskHero - podsumowanie dzienne",
                Body = $"Otwarte zgloszenia: {openCount}",
                UserId = userId
            }, ct);
        }
    }

    private async Task<IReadOnlyList<string>> GetUserIdsInRolesAsync(string[] roleNames, CancellationToken ct)
    {
        var roleIds = await _db.Roles
            .AsNoTracking()
            .Where(x => x.Name != null && roleNames.Contains(x.Name))
            .Select(x => x.Id)
            .ToListAsync(ct);

        if (roleIds.Count == 0)
            return [];

        return await _db.UserRoles
            .AsNoTracking()
            .Where(x => roleIds.Contains(x.RoleId))
            .Select(x => x.UserId)
            .Distinct()
            .ToListAsync(ct);
    }
}
