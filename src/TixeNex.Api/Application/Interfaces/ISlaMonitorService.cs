namespace TixeNex.Api.Application.Interfaces;

public interface ISlaMonitorService
{
    Task CheckBreachesAsync(CancellationToken ct = default);
}
