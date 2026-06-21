using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Auth;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class AuthService
{
    private readonly HttpClient _http;
    private readonly AuthSessionService _session;

    public AuthService(HttpClient http, AuthSessionService session)
    {
        _http = http;
        _session = session;
    }

    public async Task<bool> LoginAsync(string userName, string password, CancellationToken ct = default)
    {
        var request = new LoginRequestDto
        {
            UserName = userName,
            Password = password
        };

        var response = await _http.PostAsJsonAsync("api/auth/login", request, ct);
        if (!response.IsSuccessStatusCode)
            return false;

        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>(cancellationToken: ct);
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
            return false;

        await _session.LoginAsync(auth, ct);
        return true;
    }

    public Task LogoutAsync()
    {
        return _session.LogoutAsync();
    }
}