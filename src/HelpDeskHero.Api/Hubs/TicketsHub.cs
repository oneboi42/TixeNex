using System.Security.Claims;
using HelpDeskHero.Api.Application.TicketVisibility;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Hubs;

[Authorize]
public sealed class TicketsHub : Hub
{
    private readonly AppDbContext _db;
    private readonly ITicketVisibilityContextResolver _ticketVisibilityContextResolver;

    public TicketsHub(
        AppDbContext db,
        ITicketVisibilityContextResolver ticketVisibilityContextResolver)
    {
        _db = db;
        _ticketVisibilityContextResolver = ticketVisibilityContextResolver;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub");

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId));
        }

        await base.OnConnectedAsync();
    }

    public async Task JoinDashboard()
    {
        var visibilityContext = ResolveTicketVisibility();

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            TicketRealtimeGroups.Dashboard(visibilityContext));
    }

    public async Task JoinTicket(string ticketId)
    {
        var visibilityContext = ResolveTicketVisibility();

        if (!TryParseTicketId(ticketId, out var parsedTicketId) ||
            !await _db.Tickets
                .AsNoTracking()
                .ApplyVisibility(visibilityContext)
                .AnyAsync(ticket => ticket.Id == parsedTicketId))
        {
            throw TicketUnavailable();
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            TicketRealtimeGroups.Ticket(parsedTicketId, visibilityContext));
    }

    public async Task LeaveTicket(string ticketId)
    {
        var visibilityContext = ResolveTicketVisibility();

        if (!TryParseTicketId(ticketId, out var parsedTicketId))
            throw TicketUnavailable();

        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            TicketRealtimeGroups.Ticket(parsedTicketId, visibilityContext));
    }

    public static string UserGroup(string userId) => $"user:{userId}";

    private TicketVisibilityContext ResolveTicketVisibility()
    {
        var resolution = _ticketVisibilityContextResolver.Resolve(
            Context.User ?? new ClaimsPrincipal());

        if (resolution.Status != TicketVisibilityResolutionStatus.Resolved)
            throw new HubException("Access denied.");

        return resolution.Context!;
    }

    private static bool TryParseTicketId(string ticketId, out int parsedTicketId) =>
        int.TryParse(ticketId, out parsedTicketId) && parsedTicketId > 0;

    private static HubException TicketUnavailable() =>
        new("Ticket not found.");
}

internal static class TicketRealtimeGroups
{
    public static string Dashboard(TicketVisibilityContext context) =>
        Scoped("dashboard", context);

    public static string Ticket(
        int ticketId,
        TicketVisibilityContext context) =>
        Scoped($"ticket:{ticketId}", context);

    public static IReadOnlyCollection<string> DashboardAudience(Ticket ticket) =>
        Audience(ticket, Dashboard);

    public static IReadOnlyCollection<string> TicketAudience(Ticket ticket) =>
        Audience(ticket, context => Ticket(ticket.Id, context));

    private static IReadOnlyCollection<string> Audience(
        Ticket ticket,
        Func<TicketVisibilityContext, string> groupSelector)
    {
        var isDemoWorkspace = ticket.DemoExpiresAtUtc is not null;
        var groups = new HashSet<string>(StringComparer.Ordinal)
        {
            groupSelector(new TicketVisibilityContext(
                string.Empty,
                TicketVisibilityScope.All,
                isDemoWorkspace))
        };

        if (!string.IsNullOrWhiteSpace(ticket.RequesterUserId))
        {
            groups.Add(groupSelector(new TicketVisibilityContext(
                ticket.RequesterUserId,
                TicketVisibilityScope.Own,
                isDemoWorkspace)));

            groups.Add(groupSelector(new TicketVisibilityContext(
                ticket.RequesterUserId,
                TicketVisibilityScope.Assigned,
                isDemoWorkspace)));
        }

        if (!string.IsNullOrWhiteSpace(ticket.AssignedToUserId))
        {
            groups.Add(groupSelector(new TicketVisibilityContext(
                ticket.AssignedToUserId,
                TicketVisibilityScope.Assigned,
                isDemoWorkspace)));
        }

        return groups;
    }

    private static string Scoped(
        string prefix,
        TicketVisibilityContext context)
    {
        var workspace = context.IsDemoWorkspace ? "demo" : "normal";

        return context.Scope switch
        {
            TicketVisibilityScope.Own =>
                $"{prefix}:{workspace}:own:{context.UserId}",
            TicketVisibilityScope.Assigned =>
                $"{prefix}:{workspace}:assigned:{context.UserId}",
            TicketVisibilityScope.All =>
                $"{prefix}:{workspace}:all",
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }
}
