using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HelpDeskHero.Api.Hubs;

[Authorize]
public sealed class TicketsHub : Hub
{
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
        await Groups.AddToGroupAsync(Context.ConnectionId, "dashboard");
    }

    public async Task JoinTicket(string ticketId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"ticket:{ticketId}");
    }

    public async Task LeaveTicket(string ticketId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"ticket:{ticketId}");
    }

    public static string UserGroup(string userId) => $"user:{userId}";
}
