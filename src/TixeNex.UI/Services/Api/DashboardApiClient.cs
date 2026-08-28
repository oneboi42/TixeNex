using System.Net.Http.Json;
using TixeNex.Shared.Contracts.Dashboard;

namespace TixeNex.UI.Services.Api;

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
