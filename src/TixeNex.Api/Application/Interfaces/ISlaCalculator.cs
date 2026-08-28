using TixeNex.Api.Domain;

namespace TixeNex.Api.Application.Interfaces;

public interface ISlaCalculator
{
    Task ApplySlaAsync(Ticket ticket, CancellationToken ct = default);
}
