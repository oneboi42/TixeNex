using HelpDeskHero.Api.Application.TicketVisibility;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITicketVisibilityContextResolver _ticketVisibilityContextResolver;

    public DashboardController(
        AppDbContext db,
        ITicketVisibilityContextResolver ticketVisibilityContextResolver)
    {
        _db = db;
        _ticketVisibilityContextResolver = ticketVisibilityContextResolver;
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary(
        CancellationToken ct)
    {
        var resolution = _ticketVisibilityContextResolver.Resolve(User);

        if (resolution.Status == TicketVisibilityResolutionStatus.Unauthorized)
            return Unauthorized();

        if (resolution.Status == TicketVisibilityResolutionStatus.Forbidden)
            return Forbid();

        var visibilityContext = resolution.Context!;

        var currentUser = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == visibilityContext.UserId)
            .Select(user => new
            {
                user.IsDemoWorkspace
            })
            .SingleOrDefaultAsync(ct);

        if (currentUser is null)
            return Unauthorized();

        var activeTickets = _db.Tickets
            .AsNoTracking()
            .ApplyVisibility(visibilityContext);

        var allTickets = _db.Tickets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ApplyVisibility(visibilityContext);

        var visibleTicketIds = activeTickets.Select(ticket => ticket.Id);

        var recentAuditQuery =
            from audit in _db.AuditLogs.AsNoTracking()
            join actor in _db.Users.AsNoTracking()
                on audit.UserId equals actor.Id
            where actor.IsDemoWorkspace == currentUser.IsDemoWorkspace
            select new
            {
                Audit = audit,
                ActorDisplayName = actor.DisplayName
            };

        if (!User.IsInRole("Admin"))
        {
            recentAuditQuery = recentAuditQuery.Where(
                item => item.Audit.UserId == visibilityContext.UserId);
        }

        var dto = new DashboardSummaryDto
        {
            TotalTickets = await activeTickets.CountAsync(ct),

            OpenTickets = await activeTickets.CountAsync(
                ticket =>
                    ticket.Status == "New" ||
                    ticket.Status == "InProgress",
                ct),

            ClosedTickets = await activeTickets.CountAsync(
                ticket => ticket.Status == "Closed",
                ct),

            DeletedTickets = await allTickets.CountAsync(
                ticket => ticket.IsDeleted,
                ct),

            HighPriorityOpenTickets = await activeTickets.CountAsync(
                ticket =>
                    ticket.Priority == "High" &&
                    (ticket.Status == "New" ||
                     ticket.Status == "InProgress"),
                ct),

            TotalComments = await _db.TicketComments.CountAsync(
                comment => visibleTicketIds.Contains(comment.TicketId),
                ct),

            TotalAttachments = await _db.TicketAttachments.CountAsync(
                attachment => visibleTicketIds.Contains(attachment.TicketId),
                ct),

            UnreadNotifications = await _db.UserNotifications.CountAsync(
                notification =>
                    notification.UserId == visibilityContext.UserId &&
                    !notification.IsRead,
                ct),

            RecentAuditItems = await recentAuditQuery
                .OrderByDescending(item => item.Audit.CreatedAtUtc)
                .Take(10)
                .Select(item => new RecentAuditItemDto
                {
                    CreatedAtUtc = item.Audit.CreatedAtUtc,
                    Action = item.Audit.Action,
                    EntityName = item.Audit.EntityName,
                    PerformedBy = string.IsNullOrWhiteSpace(
                        item.ActorDisplayName)
                            ? item.Audit.UserName ?? "System"
                            : item.ActorDisplayName
                })
                .ToListAsync(ct)
        };

        return Ok(dto);
    }
}