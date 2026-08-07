using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Application.Services;

public sealed class TicketAssignmentService : ITicketAssignmentService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;

    public TicketAssignmentService(UserManager<ApplicationUser> userManager, AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public async Task<string?> AssignAsync(Ticket ticket, CancellationToken ct = default)
    {
        var agents = await _userManager.GetUsersInRoleAsync("Agent");
        var isDemoWorkspace = ticket.DemoExpiresAtUtc is not null;
        var eligibleAgents = agents
            .Where(agent =>
                agent.IsDemoWorkspace == isDemoWorkspace &&
                (!isDemoWorkspace || !agent.IsDemoUser))
            .ToList();

        if (eligibleAgents.Count == 0)
            return null;

        var agentLoads = new List<(string UserId, int ActiveTicketCount)>();

        foreach (var agent in eligibleAgents)
        {
            var activeTicketCount = await _db.Tickets.CountAsync(
                x => x.AssignedToUserId == agent.Id &&
                     (x.Status == "New" || x.Status == "InProgress") &&
                     (isDemoWorkspace
                         ? x.DemoExpiresAtUtc != null
                         : x.DemoExpiresAtUtc == null),
                ct);

            agentLoads.Add((agent.Id, activeTicketCount));
        }

        var assignedUserId = agentLoads
            .OrderBy(x => x.ActiveTicketCount)
            .ThenBy(x => x.UserId)
            .Select(x => x.UserId)
            .First();

        ticket.AssignedToUserId = assignedUserId;

        return assignedUserId;
    }
}
