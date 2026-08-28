using TixeNex.Shared.Contracts.Common;
using TixeNex.Shared.Contracts.Tickets;

namespace TixeNex.UI.Services.Api;

public interface ITicketApiClient
{
    Task<PagedResultDto<TicketDto>?> GetPageAsync(
        TicketQueryDto query,
        CancellationToken ct = default);

    Task<TicketDto?> GetByIdAsync(
        int id,
        CancellationToken ct = default);

    Task<HttpResponseMessage> CreateAsync(
        CreateTicketDto dto,
        CancellationToken ct = default);

    Task<HttpResponseMessage> UpdateAsync(
        int id,
        UpdateTicketDto dto,
        CancellationToken ct = default);

    Task<HttpResponseMessage> AssignAsync(
        int id,
        AssignTicketDto dto,
        CancellationToken ct = default);

    Task<HttpResponseMessage> StartAsync(
        int id,
        TicketLifecycleRequestDto dto,
        CancellationToken ct = default);

    Task<HttpResponseMessage> ResolveAsync(
        int id,
        TicketLifecycleRequestDto dto,
        CancellationToken ct = default);

    Task<HttpResponseMessage> CloseAsync(
        int id,
        TicketLifecycleRequestDto dto,
        CancellationToken ct = default);

    Task<HttpResponseMessage> ReopenAsync(
        int id,
        TicketLifecycleRequestDto dto,
        CancellationToken ct = default);

    Task<HttpResponseMessage> DeleteAsync(
        int id,
        CancellationToken ct = default);

    Task<IReadOnlyList<TicketDto>> GetDeletedAsync(
        CancellationToken ct = default);

    Task<HttpResponseMessage> RestoreAsync(
        int id,
        CancellationToken ct = default);

    Task<HttpResponseMessage> ExportCsvAsync(
        TicketQueryDto query,
        CancellationToken ct = default);
}
