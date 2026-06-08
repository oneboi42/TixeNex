using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.UI.Services.Api;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class AuthSessionService
{
    private readonly SessionTokenStore _tokenStore;
    private readonly AuthApiClient _authApiClient;
    private readonly JwtAuthenticationStateProvider _authStateProvider;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public AuthSessionService(
        SessionTokenStore tokenStore,
        AuthApiClient authApiClient,
        JwtAuthenticationStateProvider authStateProvider)
    {
        _tokenStore = tokenStore;
        _authApiClient = authApiClient;
        _authStateProvider = authStateProvider;
    }

    public async Task<string?> GetValidAccessTokenAsync(CancellationToken ct = default)
    {
        var auth = await _tokenStore.GetAsync(ct);
        if (auth is null)
            return null;

        if (auth.AccessTokenExpiresAtUtc > DateTime.UtcNow.AddSeconds(30))
            return auth.AccessToken;

        await _refreshLock.WaitAsync(ct);
        try
        {
            auth = await _tokenStore.GetAsync(ct);
            if (auth is null)
                return null;

            if (auth.AccessTokenExpiresAtUtc > DateTime.UtcNow.AddSeconds(30))
                return auth.AccessToken;

            var refreshed = await _authApiClient.RefreshAsync(new RefreshTokenRequestDto
            {
                AccessToken = auth.AccessToken,
                RefreshToken = auth.RefreshToken
            }, ct);

            if (refreshed is null)
            {
                await LogoutAsync(ct);
                return null;
            }

            await _tokenStore.SetAsync(refreshed, ct);
            _authStateProvider.NotifyUserAuthentication(refreshed);

            return refreshed.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    public async Task LoginAsync(AuthResponseDto auth, CancellationToken ct = default)
    {
        await _tokenStore.SetAsync(auth, ct);
        _authStateProvider.NotifyUserAuthentication(auth);
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        await _tokenStore.RemoveAsync(ct);
        _authStateProvider.NotifyUserLogout();
    }
}