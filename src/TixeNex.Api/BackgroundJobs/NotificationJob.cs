using TixeNex.Api.BackgroundJobs.Contracts;
using TixeNex.Api.Application.TicketVisibility;
using TixeNex.Api.Infrastructure.Notifications;
using TixeNex.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace TixeNex.Api.BackgroundJobs;

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
            .Where(x => x.Id == ticketId)
            .Select(x => new
            {
                x.Number,
                x.Title,
                x.RequesterUserId,
                x.AssignedToUserId
            })
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
            return;

        if (string.IsNullOrWhiteSpace(ticket.RequesterUserId))
            return;

        var requesterIsAssignee = ticket.RequesterUserId == ticket.AssignedToUserId;
        var displayId = GetDisplayTicketId(ticket.Number);

        await _dispatcher.DispatchAsync(new NotificationMessage
        {
            Channel = NotificationChannel.InApp,
            Subject = requesterIsAssignee
                ? $"Ticket created and assigned: {displayId}"
                : $"New ticket: {displayId}",
            Body = requesterIsAssignee
                ? $"Ticket {displayId} - {ticket.Title} was created and assigned to you."
                : $"Ticket {displayId} was created - {ticket.Title}",
            UserId = ticket.RequesterUserId
        }, ct);
    }

    public async Task SendDailySummaryAsync(CancellationToken ct = default)
    {
        var openTickets = _db.Tickets
            .AsNoTracking()
            .Where(ticket => !ticket.IsDeleted && ticket.Status != "Closed");

        var normalOpenCount = await openTickets
            .ApplyWorkspace(isDemoWorkspace: false)
            .CountAsync(ct);

        var demoOpenCount = await openTickets
            .ApplyWorkspace(isDemoWorkspace: true)
            .CountAsync(ct);

        var recipients = await GetUsersInRolesAsync(["Admin"], ct);

        foreach (var recipient in recipients)
        {
            var openCount = recipient.IsDemoWorkspace
                ? demoOpenCount
                : normalOpenCount;

            await _dispatcher.DispatchAsync(new NotificationMessage
            {
                Channel = NotificationChannel.InApp,
                Subject = "TixeNex - daily summary",
                Body = $"Open tickets: {openCount}",
                UserId = recipient.UserId
            }, ct);
        }
    }

    public async Task SendTicketAssignedNotificationAsync(
        int ticketId,
        string assignedToUserId,
        bool isReassignment,
        CancellationToken ct = default)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .Where(x => x.Id == ticketId)
            .Select(x => new { x.Number, x.Title })
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
            return;

        var displayId = GetDisplayTicketId(ticket.Number);

        await _dispatcher.DispatchAsync(new NotificationMessage
        {
            Channel = NotificationChannel.InApp,
            Subject = $"Ticket {(isReassignment ? "reassigned" : "assigned")}: {displayId}",
            Body = $"Ticket {displayId} - {ticket.Title} has been assigned to you.",
            UserId = assignedToUserId
        }, ct);
    }

    public async Task SendTicketDeletedNotificationsAsync(
        int ticketId,
        string deletedByUserId,
        CancellationToken ct = default)
    {
        var ticket = await _db.Tickets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.Id == ticketId && x.IsDeleted)
            .Select(x => new
            {
                x.Number,
                x.Title,
                x.RequesterUserId,
                x.AssignedToUserId
            })
            .FirstOrDefaultAsync(ct);

        if (ticket is null)
            return;

        var recipientUserIds = new HashSet<string>(StringComparer.Ordinal);

        AddRecipient(ticket.RequesterUserId);
        AddRecipient(ticket.AssignedToUserId);
        recipientUserIds.Remove(deletedByUserId);

        var displayId = GetDisplayTicketId(ticket.Number);

        foreach (var userId in recipientUserIds)
        {
            await _dispatcher.DispatchAsync(new NotificationMessage
            {
                Channel = NotificationChannel.InApp,
                Subject = $"Ticket deleted: {displayId}",
                Body = $"Ticket {displayId} - {ticket.Title} has been deleted by an administrator.",
                UserId = userId
            }, ct);
        }

        void AddRecipient(string? userId)
        {
            if (!string.IsNullOrWhiteSpace(userId))
                recipientUserIds.Add(userId);
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

        var displayId = GetDisplayTicketId(ticket.Number);
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
                Subject = $"New reply on ticket {displayId}",
                Body = $"A new reply was added to ticket {displayId} - {ticket.Title}",
                UserId = userId
            }, ct);
        }

        void AddRecipient(string? userId)
        {
            if (!string.IsNullOrWhiteSpace(userId))
                recipientUserIds.Add(userId);
        }
    }

    private static string GetDisplayTicketId(string? number)
    {
        if (string.IsNullOrWhiteSpace(number))
            return "#------";

        var suffix = number.Length <= 6
            ? number
            : number[^6..];

        return $"#{suffix}";
    }

    private async Task<IReadOnlyList<NotificationRecipient>> GetUsersInRolesAsync(
        string[] roleNames,
        CancellationToken ct)
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
            select new NotificationRecipient(
                user.Id,
                user.IsDemoWorkspace))
            .Distinct()
            .ToListAsync(ct);
    }

    private sealed record NotificationRecipient(
        string UserId,
        bool IsDemoWorkspace);
}
