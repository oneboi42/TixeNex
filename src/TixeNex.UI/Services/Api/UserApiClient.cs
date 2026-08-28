using System.Net.Http.Json;
using TixeNex.Shared.Contracts.Users;

namespace TixeNex.UI.Services.Api;

public sealed class UserApiClient
{
    private readonly HttpClient _http;

    public UserApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<AssignableAgentDto>> GetAssignableAgentsAsync(
        CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<List<AssignableAgentDto>>(
            "api/users/agents",
            ct) ?? [];
    }
}
