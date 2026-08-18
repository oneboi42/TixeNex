using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.BackgroundJobs.Contracts;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.BackgroundJobs;

public sealed class DemoCleanupJob : IDemoCleanupJob
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly IExportObjectStorage _exportObjectStorage;
    private readonly ILogger<DemoCleanupJob> _logger;

    public DemoCleanupJob(
        AppDbContext db,
        IFileStorage fileStorage,
        IExportObjectStorage exportObjectStorage,
        ILogger<DemoCleanupJob> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _exportObjectStorage = exportObjectStorage;
        _logger = logger;
    }

    public async Task CleanupExpiredDemoDataAsync(
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        var expiredTicketIds = await _db.Tickets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(ticket =>
                ticket.DemoExpiresAtUtc != null &&
                ticket.DemoExpiresAtUtc <= now)
            .Select(ticket => ticket.Id)
            .ToListAsync(ct);

        var expiredUserIds = await _db.Users
            .AsNoTracking()
            .Where(user =>
                user.IsDemoUser &&
                user.DemoAbsoluteExpiresAtUtc != null &&
                user.DemoAbsoluteExpiresAtUtc <= now)
            .Select(user => user.Id)
            .ToListAsync(ct);

        if (expiredTicketIds.Count == 0 &&
            expiredUserIds.Count == 0)
        {
            _logger.LogDebug(
                "Demo cleanup found no expired data.");

            return;
        }

        await DeleteAttachmentFilesAsync(
            expiredTicketIds,
            ct);

        await DeleteExportObjectsAsync(
            expiredUserIds,
            ct);

        await DeleteDatabaseDataAsync(
            expiredTicketIds,
            expiredUserIds,
            ct);

        _logger.LogInformation(
            "Demo cleanup completed. Deleted {TicketCount} expired tickets and {UserCount} expired temporary users.",
            expiredTicketIds.Count,
            expiredUserIds.Count);
    }

    private async Task DeleteAttachmentFilesAsync(
        IReadOnlyCollection<int> expiredTicketIds,
        CancellationToken ct)
    {
        if (expiredTicketIds.Count == 0)
            return;

        var attachmentPaths = await _db.TicketAttachments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(attachment =>
                expiredTicketIds.Contains(
                    attachment.TicketId))
            .Select(attachment =>
                attachment.RelativePath)
            .Distinct()
            .ToListAsync(ct);

        foreach (var relativePath in attachmentPaths)
        {
            await _fileStorage.DeleteAsync(
                relativePath,
                ct);
        }

        _logger.LogInformation(
            "Deleted {AttachmentCount} attachment files for expired demo tickets.",
            attachmentPaths.Count);
    }

    private async Task DeleteExportObjectsAsync(
        IReadOnlyCollection<string> expiredUserIds,
        CancellationToken ct)
    {
        if (expiredUserIds.Count == 0)
            return;

        var objectNames = await _db.ExportJobs
            .AsNoTracking()
            .Where(exportJob =>
                expiredUserIds.Contains(
                    exportJob.UserId) &&
                exportJob.StorageObjectName != null)
            .Select(exportJob =>
                exportJob.StorageObjectName!)
            .Distinct()
            .ToListAsync(ct);

        foreach (var objectName in objectNames)
        {
            await _exportObjectStorage.DeleteAsync(
                objectName,
                ct);
        }

        _logger.LogInformation(
            "Deleted {ExportCount} export objects for expired demo users.",
            objectNames.Count);
    }

    private async Task DeleteDatabaseEntitiesRelationalAsync(
        IReadOnlyCollection<int> expiredTicketIds,
        IReadOnlyCollection<string> expiredUserIds,
        CancellationToken ct)
    {
        var ticketIds = expiredTicketIds.ToArray();
        var userIds = expiredUserIds.ToArray();

        var ticketEntityIds = ticketIds
            .Select(id => id.ToString())
            .ToArray();

        if (ticketIds.Length > 0)
        {
            await _db.AuditLogs
                .Where(log =>
                    log.EntityName == "Ticket" &&
                    ticketEntityIds.Contains(log.EntityId))
                .ExecuteDeleteAsync(ct);

            await _db.Tickets
                .IgnoreQueryFilters()
                .Where(ticket =>
                    ticketIds.Contains(ticket.Id))
                .ExecuteDeleteAsync(ct);
        }

        if (userIds.Length == 0)
            return;

        await _db.Tickets
            .IgnoreQueryFilters()
            .Where(ticket =>
                !ticketIds.Contains(ticket.Id) &&
                ticket.RequesterUserId != null &&
                userIds.Contains(ticket.RequesterUserId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    ticket => ticket.RequesterUserId,
                    (string?)null),
                ct);

        await _db.Tickets
            .IgnoreQueryFilters()
            .Where(ticket =>
                !ticketIds.Contains(ticket.Id) &&
                ticket.AssignedToUserId != null &&
                userIds.Contains(ticket.AssignedToUserId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    ticket => ticket.AssignedToUserId,
                    (string?)null),
                ct);

        await _db.Tickets
            .IgnoreQueryFilters()
            .Where(ticket =>
                !ticketIds.Contains(ticket.Id) &&
                ticket.DeletedByUserId != null &&
                userIds.Contains(ticket.DeletedByUserId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    ticket => ticket.DeletedByUserId,
                    (string?)null),
                ct);

        await _db.TicketEscalations
            .IgnoreQueryFilters()
            .Where(escalation =>
                !ticketIds.Contains(escalation.TicketId) &&
                escalation.AssignedToUserId != null &&
                userIds.Contains(escalation.AssignedToUserId))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    escalation => escalation.AssignedToUserId,
                    (string?)null),
                ct);

        await _db.UserNotifications
            .Where(notification =>
                notification.UserId != null &&
                userIds.Contains(notification.UserId))
            .ExecuteDeleteAsync(ct);

        await _db.AuditLogs
            .Where(log =>
                log.UserId != null &&
                userIds.Contains(log.UserId))
            .ExecuteDeleteAsync(ct);

        await _db.ExportJobs
            .Where(exportJob =>
                userIds.Contains(exportJob.UserId))
            .ExecuteDeleteAsync(ct);

        await _db.RefreshTokens
            .Where(refreshToken =>
                userIds.Contains(refreshToken.UserId))
            .ExecuteDeleteAsync(ct);

        await _db.UserClaims
            .Where(claim =>
                userIds.Contains(claim.UserId))
            .ExecuteDeleteAsync(ct);

        await _db.UserLogins
            .Where(login =>
                userIds.Contains(login.UserId))
            .ExecuteDeleteAsync(ct);

        await _db.UserTokens
            .Where(token =>
                userIds.Contains(token.UserId))
            .ExecuteDeleteAsync(ct);

        await _db.UserRoles
            .Where(role =>
                userIds.Contains(role.UserId))
            .ExecuteDeleteAsync(ct);

        await _db.Users
            .Where(user =>
                userIds.Contains(user.Id))
            .ExecuteDeleteAsync(ct);
    }

    private async Task DeleteDatabaseDataAsync(
        IReadOnlyCollection<int> expiredTicketIds,
        IReadOnlyCollection<string> expiredUserIds,
        CancellationToken ct)
    {
        if (_db.Database.IsRelational())
        {
            await using var transaction =
                await _db.Database.BeginTransactionAsync(ct);

            await DeleteDatabaseEntitiesRelationalAsync(
                expiredTicketIds,
                expiredUserIds,
                ct);

            await transaction.CommitAsync(ct);
            return;
        }
        await DeleteDatabaseEntitiesAsync(
            expiredTicketIds,
            expiredUserIds,
            ct);
    }

    private async Task DeleteDatabaseEntitiesAsync(
        IReadOnlyCollection<int> expiredTicketIds,
        IReadOnlyCollection<string> expiredUserIds,
        CancellationToken ct)
    {
        var ticketIds = expiredTicketIds.ToArray();
        var userIds = expiredUserIds.ToArray();
        var ticketEntityIds = ticketIds
            .Select(id => id.ToString())
            .ToArray();

        if (ticketIds.Length > 0)
        {

            var ticketAuditLogs = await _db.AuditLogs
                .Where(log =>
                    log.EntityName == "Ticket" &&
                    ticketEntityIds.Contains(log.EntityId))
                .ToListAsync(ct);

            var comments = await _db.TicketComments
                .IgnoreQueryFilters()
                .Where(comment =>
                    ticketIds.Contains(comment.TicketId))
                .ToListAsync(ct);

            var attachments = await _db.TicketAttachments
                .IgnoreQueryFilters()
                .Where(attachment =>
                    ticketIds.Contains(attachment.TicketId))
                .ToListAsync(ct);

            var escalations = await _db.TicketEscalations
                .IgnoreQueryFilters()
                .Where(escalation =>
                    ticketIds.Contains(escalation.TicketId))
                .ToListAsync(ct);

            var tickets = await _db.Tickets
                .IgnoreQueryFilters()
                .Where(ticket =>
                    ticketIds.Contains(ticket.Id))
                .ToListAsync(ct);

            _db.AuditLogs.RemoveRange(ticketAuditLogs);
            _db.TicketComments.RemoveRange(comments);
            _db.TicketAttachments.RemoveRange(attachments);
            _db.TicketEscalations.RemoveRange(escalations);
            _db.Tickets.RemoveRange(tickets);
        }

        if (userIds.Length > 0)
        {
            // RequesterUserId uses SetNull in SQL Server, while AssignedToUserId
            // is only a string. Clear both explicitly so behavior is identical
            // under SQL Server and EF Core InMemory and no dangling IDs remain.
            var survivingTickets = await _db.Tickets
                .IgnoreQueryFilters()
                .Where(ticket =>
                    !ticketIds.Contains(ticket.Id) &&
                    ((ticket.RequesterUserId != null &&
                      userIds.Contains(ticket.RequesterUserId)) ||
                     (ticket.AssignedToUserId != null &&
                      userIds.Contains(ticket.AssignedToUserId)) ||
                     (ticket.DeletedByUserId != null &&
                      userIds.Contains(ticket.DeletedByUserId))))
                .ToListAsync(ct);

            foreach (var ticket in survivingTickets)
            {
                if (ticket.RequesterUserId != null &&
                    userIds.Contains(ticket.RequesterUserId))
                {
                    ticket.RequesterUserId = null;
                }

                if (ticket.AssignedToUserId != null &&
                    userIds.Contains(ticket.AssignedToUserId))
                {
                    ticket.AssignedToUserId = null;
                }

                if (ticket.DeletedByUserId != null &&
                    userIds.Contains(ticket.DeletedByUserId))
                {
                    ticket.DeletedByUserId = null;
                }
            }

            var survivingEscalations = await _db.TicketEscalations
                .IgnoreQueryFilters()
                .Where(escalation =>
                    !ticketIds.Contains(escalation.TicketId) &&
                    escalation.AssignedToUserId != null &&
                    userIds.Contains(escalation.AssignedToUserId))
                .ToListAsync(ct);

            foreach (var escalation in survivingEscalations)
            {
                escalation.AssignedToUserId = null;
            }

            var notifications = await _db.UserNotifications
                .Where(notification =>
                    notification.UserId != null &&
                    userIds.Contains(notification.UserId))
                .ToListAsync(ct);

            var userAuditLogs = await _db.AuditLogs
                .Where(log =>
                    log.UserId != null &&
                    userIds.Contains(log.UserId) &&
                    !(log.EntityName == "Ticket" &&
                      ticketEntityIds.Contains(log.EntityId)))
                .ToListAsync(ct);

            var exportJobs = await _db.ExportJobs
                .Where(exportJob =>
                    userIds.Contains(exportJob.UserId))
                .ToListAsync(ct);

            var refreshTokens = await _db.RefreshTokens
                .Where(refreshToken =>
                    userIds.Contains(refreshToken.UserId))
                .ToListAsync(ct);

            // Explicitly remove ASP.NET Identity dependants too. SQL Server can
            // cascade these rows, but doing it here keeps InMemory tests aligned
            // with production cleanup behavior.
            var userClaims = await _db.UserClaims
                .Where(claim => userIds.Contains(claim.UserId))
                .ToListAsync(ct);

            var userLogins = await _db.UserLogins
                .Where(login => userIds.Contains(login.UserId))
                .ToListAsync(ct);

            var userTokens = await _db.UserTokens
                .Where(token => userIds.Contains(token.UserId))
                .ToListAsync(ct);

            var userRoles = await _db.UserRoles
                .Where(role => userIds.Contains(role.UserId))
                .ToListAsync(ct);

            var users = await _db.Users
                .Where(user => userIds.Contains(user.Id))
                .ToListAsync(ct);

            _db.UserNotifications.RemoveRange(notifications);
            _db.AuditLogs.RemoveRange(userAuditLogs);
            _db.ExportJobs.RemoveRange(exportJobs);
            _db.RefreshTokens.RemoveRange(refreshTokens);
            _db.UserClaims.RemoveRange(userClaims);
            _db.UserLogins.RemoveRange(userLogins);
            _db.UserTokens.RemoveRange(userTokens);
            _db.UserRoles.RemoveRange(userRoles);
            _db.Users.RemoveRange(users);
        }

        await _db.SaveChangesForCleanupAsync(ct);
    }
}