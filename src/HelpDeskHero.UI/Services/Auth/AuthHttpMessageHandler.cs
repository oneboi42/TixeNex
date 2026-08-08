using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using HelpDeskHero.Shared.Contracts.Auth;
using HelpDeskHero.UI.Services.Realtime;
using Microsoft.AspNetCore.Components;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class AuthHttpMessageHandler : DelegatingHandler
{
    private readonly TokenStore _tokenStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly NavigationManager _navigationManager;
    private readonly JwtAuthenticationStateProvider _authStateProvider;
    private readonly NotificationSessionState _notificationSessionState;

    public AuthHttpMessageHandler(
        TokenStore tokenStore,
        IHttpClientFactory httpClientFactory,
        NavigationManager navigationManager,
        JwtAuthenticationStateProvider authStateProvider,
        NotificationSessionState notificationSessionState)
    {
        _tokenStore = tokenStore;
        _httpClientFactory = httpClientFactory;
        _navigationManager = navigationManager;
        _authStateProvider = authStateProvider;
        _notificationSessionState = notificationSessionState;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var accessToken = await _tokenStore.GetAccessTokenAsync();

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            if (IsTokenExpiredOrCloseToExpiry(accessToken))
            {
                await TryRefreshAsync(cancellationToken);
                accessToken = await _tokenStore.GetAccessTokenAsync();
            }

            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", accessToken);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var refreshed = await TryRefreshAsync(cancellationToken);

        if (!refreshed)
        {
            await LogoutAndRedirectAsync();
            return response;
        }

        accessToken = await _tokenStore.GetAccessTokenAsync();
        var clone = await CloneRequestAsync(request);

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            clone.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
        }

        response.Dispose();
        return await base.SendAsync(clone, cancellationToken);
    }

    private static bool IsTokenExpiredOrCloseToExpiry(string accessToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = handler.ReadJwtToken(accessToken);

            return jwt.ValidTo <= DateTime.UtcNow.AddMinutes(1);
        }
        catch
        {
            return true;
        }
    }

    private async Task<bool> TryRefreshAsync(CancellationToken ct)
    {
        var refreshToken = await _tokenStore.GetRefreshTokenAsync();

        if (string.IsNullOrWhiteSpace(refreshToken))
            return false;

        var client = _httpClientFactory.CreateClient("AnonymousApi");

        var result = await client.PostAsJsonAsync("api/auth/refresh", new RefreshRequestDto
        {
            RefreshToken = refreshToken,
            DeviceName = Environment.MachineName
        }, ct);

        if (!result.IsSuccessStatusCode)
        {
            await _tokenStore.ClearAsync();
            return false;
        }

        var dto = await result.Content.ReadFromJsonAsync<TokenResponseDto>(
            cancellationToken: ct);

        if (dto is null)
        {
            await _tokenStore.ClearAsync();
            return false;
        }

        try
        {
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(dto.AccessToken);

            await _tokenStore.SetAuthenticationAsync(dto);
            _authStateProvider.NotifyUserAuthentication(jwt.Claims);
        }
        catch
        {
            await _tokenStore.ClearAsync();
            return false;
        }

        return true;
    }

    private async Task LogoutAndRedirectAsync()
    {
        await _tokenStore.ClearAsync();

        try
        {
            await _notificationSessionState.ResetAsync();
        }
        finally
        {
            _authStateProvider.NotifyUserLogout();
        }

        _navigationManager.NavigateTo("/login", forceLoad: false);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var bytes = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(bytes);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(
                    header.Key,
                    header.Value);
            }
        }

        return clone;
    }
}