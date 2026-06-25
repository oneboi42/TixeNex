using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Dashboard;

namespace HelpDeskHero.UI.Services.Api;

public sealed class DashboardApiClient
{
    private readonly HttpClient _http;

    public DashboardApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<DashboardSummaryDto?> GetSummaryAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<DashboardSummaryDto>("api/dashboard/summary", ct);
    }
}
