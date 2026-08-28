using System.Net.Http.Json;
using TixeNex.Shared.Contracts.Tickets;

namespace TixeNex.UI.Services.Api;

public sealed class TicketCommentApiClient
{
    private readonly HttpClient _http;

    public TicketCommentApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<TicketCommentDto>> GetAllAsync(
        int ticketId,
        CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<TicketCommentDto>>(
            $"api/tickets/{ticketId}/comments",
            ct) ?? [];
    }

    public async Task<HttpResponseMessage> CreateAsync(
        int ticketId,
        CreateTicketCommentDto dto,
        CancellationToken ct = default)
    {
        return await _http.PostAsJsonAsync($"api/tickets/{ticketId}/comments", dto, ct);
    }
}
