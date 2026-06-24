using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;

namespace HelpDeskHero.UI.Services.Api;

public sealed class TicketApiClient
{
    private readonly HttpClient _http;

    public TicketApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<PagedResultDto<TicketDto>?> GetPageAsync(
        TicketQueryDto query,
        CancellationToken ct = default)
    {
        var url =
            $"api/tickets?pageNumber={query.PageNumber}&pageSize={query.PageSize}" +
            $"&search={Uri.EscapeDataString(query.Search ?? string.Empty)}" +
            $"&status={Uri.EscapeDataString(query.Status ?? string.Empty)}" +
            $"&priority={Uri.EscapeDataString(query.Priority ?? string.Empty)}" +
            $"&sortBy={Uri.EscapeDataString(query.SortBy)}" +
            $"&desc={query.Desc}";

        return await _http.GetFromJsonAsync<PagedResultDto<TicketDto>>(url, ct);
    }

    public Task<TicketDto?> GetByIdAsync(int id, CancellationToken ct = default) =>
    _http.GetFromJsonAsync<TicketDto>($"api/tickets/{id}", ct);

    public async Task<HttpResponseMessage> CreateAsync(
        CreateTicketDto dto,
        CancellationToken ct = default) =>
        await _http.PostAsJsonAsync("api/tickets", dto, ct);

    public async Task<HttpResponseMessage> UpdateAsync(
        int id,
        UpdateTicketDto dto,
        CancellationToken ct = default) =>
        await _http.PutAsJsonAsync($"api/tickets/{id}", dto, ct);

    public async Task<HttpResponseMessage> DeleteAsync(
        int id,
        CancellationToken ct = default) =>
        await _http.DeleteAsync($"api/tickets/{id}", ct);
}