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

    public async Task SendTicketCommentNotificationsAsync(
        int ticketId,
        string authorUserId,
        CancellationToken ct = default)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .Where(x => x.Id == ticketId)
            .Select(x => new { x.Number, x.Title, x.AssignedToUserId })
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
            return;

        var recipientUserIds = new HashSet<string>(StringComparer.Ordinal);

        // Audit data already records the ticket creator, so no extra ownership column is needed.
        var ticketCreatorUserId = await _db.AuditLogs
            .AsNoTracking()
            .Where(x => x.EntityName == "Ticket" &&
                        x.EntityId == ticketId.ToString() &&
                        x.Action == "Create" &&
                        x.UserId != null)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.UserId)
            .FirstOrDefaultAsync(ct);

        // Older tickets may predate ticket-create audit records. The first message is the
        // best available participant relationship in that legacy case.
        ticketCreatorUserId ??= await _db.TicketComments
            .AsNoTracking()
            .Where(x => x.TicketId == ticketId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => x.CreatedByUserId)
            .FirstOrDefaultAsync(ct);

        // The creator, assigned agent, and previous commenters are the ticket participants.
        AddRecipient(ticketCreatorUserId);
        AddRecipient(ticket.AssignedToUserId);

        var participantUserIds = await _db.TicketComments
            .AsNoTracking()
            .Where(x => x.TicketId == ticketId)
            .Select(x => x.CreatedByUserId)
            .Distinct()
            .ToListAsync(ct);

        foreach (var participantUserId in participantUserIds)
            AddRecipient(participantUserId);

        recipientUserIds.Remove(authorUserId);

        var activeRecipientUserIds = await _db.Users
            .AsNoTracking()
            .Where(x => x.IsActive && recipientUserIds.Contains(x.Id))
            .Select(x => x.Id)
            .ToListAsync(ct);

        foreach (var userId in activeRecipientUserIds)
        {
            await _dispatcher.DispatchAsync(new NotificationMessage
            {
                Channel = NotificationChannel.InApp,
                Subject = $"Nowa odpowiedz w zgloszeniu {ticket.Number}",
                Body = $"Otrzymano nowa odpowiedz w zgloszeniu {ticket.Number} - {ticket.Title}",
                UserId = userId
            }, ct);
        }

        void AddRecipient(string? userId)
        {
            if (!string.IsNullOrWhiteSpace(userId))
                recipientUserIds.Add(userId);
        }
    }

    private async Task<IReadOnlyList<string>> GetUserIdsInRolesAsync(string[] roleNames, CancellationToken ct)
    {
        var normalizedRoleNames = roleNames
            .Select(x => x.Trim().ToUpperInvariant())
            .ToArray();

        return await (
            from userRole in _db.UserRoles.AsNoTracking()
            join role in _db.Roles.AsNoTracking() on userRole.RoleId equals role.Id
            join user in _db.Users.AsNoTracking() on userRole.UserId equals user.Id
            where role.NormalizedName != null
                && normalizedRoleNames.Contains(role.NormalizedName)
                && user.IsActive
            select user.Id)
            .Distinct()
            .ToListAsync(ct);
    }
}
