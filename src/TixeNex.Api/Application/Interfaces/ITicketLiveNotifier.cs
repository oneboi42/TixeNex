using TixeNex.Shared.Contracts.Tickets;

namespace TixeNex.Api.Application.Interfaces;

public interface ITicketLiveNotifier
{
    Task NotifyTicketChangedAsync(TicketLiveUpdateDto dto, CancellationToken ct = default);
}
