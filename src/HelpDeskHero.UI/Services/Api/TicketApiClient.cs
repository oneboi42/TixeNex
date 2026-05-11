using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Tickets;

namespace HelpDeskHero.UI.Services.Api;

public sealed class TicketApiClient
{
    private readonly HttpClient _httpClient;

    public TicketApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<TicketDto>> GetAllAsync(CancellationToken ct = default)
    {
        var result = await _httpClient.GetFromJsonAsync<List<TicketDto>>("api/tickets", ct);
        return result ?? [];
    }

    public async Task<TicketDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync<TicketDto>($"api/tickets/{id}", ct);
    }

    public async Task<TicketDto?> CreateAsync(CreateTicketDto dto, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/tickets", dto, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TicketDto>(cancellationToken: ct);
    }
}