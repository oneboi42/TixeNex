using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Dashboard;

namespace HelpDeskHero.UI.Services.Api;

public sealed class DashboardApiClient
{
    private readonly HttpClient _httpClient;

    public DashboardApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<DashboardSummaryDto?> GetSummaryAsync(CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync<DashboardSummaryDto>("api/dashboard/summary", ct);
    }
}