using TixeNex.Api.Application.Interfaces;
using TixeNex.Api.Application.TicketVisibility;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace TixeNex.Api.Application.Services;

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
        var isDemoWorkspace = ticket.IsDemoWorkspace();
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
            var activeTicketCount = await _db.Tickets
                .ApplyWorkspace(isDemoWorkspace)
                .CountAsync(
                    x => x.AssignedToUserId == agent.Id &&
                         (x.Status == "New" || x.Status == "InProgress"),
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
