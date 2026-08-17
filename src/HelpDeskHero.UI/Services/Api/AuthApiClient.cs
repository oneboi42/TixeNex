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

    public async Task<bool> LoginAsync(
        LoginRequestDto dto,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("AnonymousApi");
        var response = await client.PostAsJsonAsync("api/auth/login", dto, ct);

        if (!response.IsSuccessStatusCode)
            return false;

        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>(
            cancellationToken: ct);

        return token is not null &&
            await CompleteAuthenticationAsync(token, ct);
    }

    public async Task<bool> StartDemoSessionAsync(
        string role,
        string deviceName,
        CancellationToken ct = default)
    {
        var client = _httpClientFactory.CreateClient("AnonymousApi");

        var response = await client.PostAsJsonAsync(
            "api/demo/sessions",
            new CreateDemoSessionRequestDto
            {
                Role = role,
                DeviceName = deviceName
            },
            ct);

        if (!response.IsSuccessStatusCode)
            return false;

        var token = await response.Content.ReadFromJsonAsync<TokenResponseDto>(
            cancellationToken: ct);

        return token is not null &&
            await CompleteAuthenticationAsync(token, ct);
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

    private async Task<bool> CompleteAuthenticationAsync(
        TokenResponseDto token,
        CancellationToken ct)
    {
        JwtSecurityToken jwt;

        try
        {
            jwt = new JwtSecurityTokenHandler().ReadJwtToken(token.AccessToken);
        }
        catch
        {
            await _tokenStore.ClearAsync();
            _authStateProvider.NotifyUserLogout();
            return false;
        }

        var userId = jwt.Claims
            .FirstOrDefault(x => x.Type == ClaimTypes.NameIdentifier)?.Value
            ?? jwt.Claims.FirstOrDefault(x => x.Type == "sub")?.Value;

        if (string.IsNullOrWhiteSpace(userId))
        {
            await _tokenStore.ClearAsync();
            _authStateProvider.NotifyUserLogout();
            return false;
        }

        await _tokenStore.SetAuthenticationAsync(token);
        _authStateProvider.NotifyUserAuthentication(jwt.Claims);
        await _notificationRealtime.TryStartAsync(userId, ct);

        return true;
    }
}