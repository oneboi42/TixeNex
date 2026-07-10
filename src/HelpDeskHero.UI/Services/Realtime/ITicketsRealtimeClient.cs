using HelpDeskHero.Shared.Contracts.Tickets;

namespace HelpDeskHero.UI.Services.Realtime;

public interface ITicketsRealtimeClient
{
    event Func<TicketLiveUpdateDto, Task>? OnTicketChanged;

    Task StartAsync(CancellationToken ct = default);
}
