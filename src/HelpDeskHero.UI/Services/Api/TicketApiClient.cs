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

    public Task<TicketDetailsDto?> GetByIdAsync(int id, CancellationToken ct = default)
        => _httpClient.GetFromJsonAsync<TicketDetailsDto>($"api/tickets/{id}", ct);

    public async Task<TicketDetailsDto?> CreateAsync(CreateTicketDto dto, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/tickets", dto, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TicketDetailsDto>(cancellationToken: ct);
    }

    public async Task UpdateAsync(int id, UpdateTicketDto dto, CancellationToken ct = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/tickets/{id}", dto, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"api/tickets/{id}", ct);
        response.EnsureSuccessStatusCode();
    }
}