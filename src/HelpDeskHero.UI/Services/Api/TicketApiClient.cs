using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Common;
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
        var response = await _httpClient.GetAsync("api/tickets", ct);
        await HandleResponseErrorAsync(response, ct);
        
        var result = await response.Content.ReadFromJsonAsync<List<TicketDto>>(cancellationToken: ct);
        return result ?? [];
    }

    public async Task<TicketDetailsDto?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        var response = await _httpClient.GetAsync($"api/tickets/{id}", ct);
        await HandleResponseErrorAsync(response, ct);
        
        return await response.Content.ReadFromJsonAsync<TicketDetailsDto>(cancellationToken: ct);
    }

    public async Task<TicketDetailsDto?> CreateAsync(CreateTicketDto dto, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/tickets", dto, ct);
        await HandleResponseErrorAsync(response, ct);
        
        return await response.Content.ReadFromJsonAsync<TicketDetailsDto>(cancellationToken: ct);
    }

    public async Task UpdateAsync(int id, UpdateTicketDto dto, CancellationToken ct = default)
    {
        var response = await _httpClient.PutAsJsonAsync($"api/tickets/{id}", dto, ct);
        await HandleResponseErrorAsync(response, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        var response = await _httpClient.DeleteAsync($"api/tickets/{id}", ct);
        await HandleResponseErrorAsync(response, ct);
    }

    private static async Task HandleResponseErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        // If 200-299, just exit the helper
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int statusCode = (int)response.StatusCode;
        ApiErrorDto? errorDto = null;

        try
        {
            // We are trying to read JSON with error details from the backend.
            errorDto = await response.Content.ReadFromJsonAsync<ApiErrorDto>(cancellationToken: ct);
        }
        catch
        {
            // If parsing fails, we ignore the error and continue
        }

        throw new ApiException($"The API request failed: {statusCode}", statusCode, errorDto);
    }
}