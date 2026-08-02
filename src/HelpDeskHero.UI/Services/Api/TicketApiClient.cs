using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;

namespace HelpDeskHero.UI.Services.Api;

public sealed class TicketApiClient : ITicketApiClient
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

    public async Task<HttpResponseMessage> AssignAsync(
        int id,
        AssignTicketDto dto,
        CancellationToken ct = default) =>
        await _http.PostAsJsonAsync($"api/tickets/{id}/assign", dto, ct);

    public Task<HttpResponseMessage> StartAsync(
        int id,
        TicketLifecycleRequestDto dto,
        CancellationToken ct = default) =>
        PostLifecycleAsync(id, "start", dto, ct);

    public Task<HttpResponseMessage> ResolveAsync(
        int id,
        TicketLifecycleRequestDto dto,
        CancellationToken ct = default) =>
        PostLifecycleAsync(id, "resolve", dto, ct);

    public Task<HttpResponseMessage> CloseAsync(
        int id,
        TicketLifecycleRequestDto dto,
        CancellationToken ct = default) =>
        PostLifecycleAsync(id, "close", dto, ct);

    public Task<HttpResponseMessage> ReopenAsync(
        int id,
        TicketLifecycleRequestDto dto,
        CancellationToken ct = default) =>
        PostLifecycleAsync(id, "reopen", dto, ct);

    public async Task<HttpResponseMessage> DeleteAsync(
        int id,
        CancellationToken ct = default) =>
        await _http.DeleteAsync($"api/tickets/{id}", ct);

    public async Task<IReadOnlyList<TicketDto>> GetDeletedAsync(
        CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<TicketDto>>(
            "api/tickets/deleted",
            ct) ?? [];
    }

    public async Task<HttpResponseMessage> RestoreAsync(
        int id,
        CancellationToken ct = default)
    {
        return await _http.PostAsync($"api/tickets/{id}/restore", content: null, ct);
    }

    public async Task<HttpResponseMessage> ExportCsvAsync(
        TicketQueryDto query,
        CancellationToken ct = default)
    {
        var parameters = new List<string>();

        if (!string.IsNullOrWhiteSpace(query.Status))
            parameters.Add($"status={Uri.EscapeDataString(query.Status)}");

        if (!string.IsNullOrWhiteSpace(query.Priority))
            parameters.Add($"priority={Uri.EscapeDataString(query.Priority)}");

        var queryString = parameters.Count > 0
            ? "?" + string.Join("&", parameters)
            : "";

        return await _http.GetAsync($"api/tickets/export{queryString}", ct);
    }

    private async Task<HttpResponseMessage> PostLifecycleAsync(
        int id,
        string action,
        TicketLifecycleRequestDto dto,
        CancellationToken ct)
    {
        return await _http.PostAsJsonAsync($"api/tickets/{id}/{action}", dto, ct);
    }
}
