using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Auth;

namespace HelpDeskHero.UI.Services.Api;

public sealed class AuthApiClient
{
    private readonly HttpClient _httpClient;

    public AuthApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginRequestDto dto, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/auth/login", dto, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<AuthResponseDto>(cancellationToken: ct);
    }

    public async Task<AuthResponseDto?> RefreshAsync(RefreshTokenRequestDto dto, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("api/auth/refresh", dto, ct);
        if (!response.IsSuccessStatusCode)
            return null;

        return await response.Content.ReadFromJsonAsync<AuthResponseDto>(cancellationToken: ct);
    }
}