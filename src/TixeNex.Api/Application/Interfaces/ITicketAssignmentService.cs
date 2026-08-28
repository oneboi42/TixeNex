using TixeNex.Api.Domain;

namespace TixeNex.Api.Application.Interfaces;

public interface ITicketAssignmentService
{
    Task<string?> AssignAsync(Ticket ticket, CancellationToken ct = default);
}
