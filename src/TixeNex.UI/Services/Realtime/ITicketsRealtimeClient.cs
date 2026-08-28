using TixeNex.Shared.Contracts.Tickets;

namespace TixeNex.UI.Services.Realtime;

public interface ITicketsRealtimeClient
{
    event Func<TicketLiveUpdateDto, Task>? OnTicketChanged;

    Task StartAsync(CancellationToken ct = default);
}
