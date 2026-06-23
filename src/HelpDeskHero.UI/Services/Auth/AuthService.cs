using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Auth;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class AuthService
{
    private readonly HttpClient _http;
    private readonly TokenStore _tokenStore;
    private readonly JwtAuthenticationStateProvider _authStateProvider;

    public AuthService(
        HttpClient http,
        TokenStore tokenStore,
        JwtAuthenticationStateProvider authStateProvider)
    {
        _http = http;
        _tokenStore = tokenStore;
        _authStateProvider = authStateProvider;
    }

    public async Task<bool> LoginAsync(
        string userName,
        string password,
        CancellationToken ct = default)
    {
        var request = new LoginRequestDto
        {
            UserName = userName,
            Password = password
        };

        var response = await _http.PostAsJsonAsync("api/auth/login", request, ct);

        if (!response.IsSuccessStatusCode)
            return false;

        var auth = await response.Content.ReadFromJsonAsync<AuthResponseDto>(
            cancellationToken: ct);

        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
            return false;

        await _tokenStore.SetAccessTokenAsync(auth.AccessToken);

        if (!string.IsNullOrWhiteSpace(auth.RefreshToken))
        {
            await _tokenStore.SetRefreshTokenAsync(auth.RefreshToken);
        }

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(auth.AccessToken);

        _authStateProvider.NotifyUserAuthentication(jwt.Claims);

        return true;
    }

    public async Task LogoutAsync()
    {
        await _tokenStore.ClearAsync();
        _authStateProvider.NotifyUserLogout();
    }
}