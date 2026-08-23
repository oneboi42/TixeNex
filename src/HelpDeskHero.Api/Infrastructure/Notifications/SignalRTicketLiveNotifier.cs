using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Hubs;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Infrastructure.Notifications;

public sealed class SignalRTicketLiveNotifier : ITicketLiveNotifier
{
    private readonly IHubContext<TicketsHub> _hubContext;
    private readonly AppDbContext _db;

    public SignalRTicketLiveNotifier(
        IHubContext<TicketsHub> hubContext,
        AppDbContext db)
    {
        _hubContext = hubContext;
        _db = db;
    }

    public async Task NotifyTicketChangedAsync(TicketLiveUpdateDto dto, CancellationToken ct = default)
    {
        var ticket = await _db.Tickets
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(ticket => ticket.Id == dto.TicketId, ct);

        if (ticket is null)
            return;

        foreach (var group in TicketRealtimeGroups.DashboardAudience(ticket))
        {
            await _hubContext.Clients.Group(group)
                .SendAsync("TicketChanged", dto, ct);
        }

        foreach (var group in TicketRealtimeGroups.TicketAudience(ticket))
        {
            await _hubContext.Clients.Group(group)
                .SendAsync("TicketChanged", dto, ct);
        }
    }
}
