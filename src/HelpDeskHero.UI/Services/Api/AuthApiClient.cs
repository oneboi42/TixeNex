using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Security.Claims;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.UI.Services.Auth;
using HelpDeskHero.UI.Services.Realtime;

namespace HelpDeskHero.UI.Services.Api;

public sealed class AuthApiClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TokenStore _tokenStore;
    private readonly JwtAuthenticationStateProvider _authStateProvider;
    private readonly NotificationRealtimeCoordinator _notificationRealtime;

    public AuthApiClient(
        IHttpClientFactory httpClientFactory,
        TokenStore tokenStore,
        JwtAuthenticationStateProvider authStateProvider,
        NotificationRealtimeCoordinator notificationRealtime)
    {
        _httpClientFactory = httpClientFactory;
        _tokenStore = tokenStore;
        _authStateProvider = authStateProvider;
        _notificationRealtime = notificationRealtime;
    }

    public async Task<bool> LoginAsync(LoginRequestDto dto, CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("AnonymousApi");

        var response = await client.PostAsJsonAsync("api/auth/login", dto, ct);

        if (!response.IsSuccessStatusCode)
            return false;

        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>(
            cancellationToken: ct);

        if (token is null)
            return false;

        await _tokenStore.SetAccessTokenAsync(token.AccessToken);
        await _tokenStore.SetRefreshTokenAsync(token.RefreshToken);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);
        var userId = jwt.Claims.FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value
            ?? jwt.Claims.FirstOrDefault(x => x.Type == "sub")?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            await _tokenStore.ClearAsync();
            _authStateProvider.NotifyUserLogout();
            return false;
        }

        _authStateProvider.NotifyUserAuthentication(jwt.Claims);
        await _notificationRealtime.StartAsync(userId, ct);

        return true;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("AuthorizedApi");
            var refresh = await _tokenStore.GetRefreshTokenAsync();

            if (!string.IsNullOrWhiteSpace(refresh))
            {
                await client.PostAsJsonAsync("api/auth/logout", new RefreshRequestDto
                {
                    RefreshToken = refresh,
                    DeviceName = Environment.MachineName
                }, ct);
            }
        }
        finally
        {
            await _tokenStore.ClearAsync();

            try
            {
                await _notificationRealtime.ResetAsync();
            }
            finally
            {
                _authStateProvider.NotifyUserLogout();
            }
        }
    }
    public async Task RevokeAllSessionsAsync(CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("AuthorizedApi");

        var response = await client.PostAsync("api/auth/revoke-all", null, ct);

        response.EnsureSuccessStatusCode();
    }

}
