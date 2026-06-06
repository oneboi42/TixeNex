using System.Security.Claims;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Components.Authorization;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly SessionTokenStore _tokenStore;

    public JwtAuthenticationStateProvider(SessionTokenStore tokenStore)
    {
        _tokenStore = tokenStore;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var auth = await _tokenStore.GetAsync();
        if (auth is null || string.IsNullOrWhiteSpace(auth.AccessToken))
            return Anonymous();

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, auth.UserName),
            new(ClaimTypes.Role, auth.Role)
        };

        var identity = new ClaimsIdentity(claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void NotifyUserAuthentication(AuthResponseDto auth)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, auth.UserName),
            new(ClaimTypes.Role, auth.Role)
        };

        var identity = new ClaimsIdentity(claims, "jwt");
        NotifyAuthenticationStateChanged(
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity))));
    }

    public void NotifyUserLogout()
    {
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous()));
    }

    private static AuthenticationState Anonymous() =>
        new(new ClaimsPrincipal(new ClaimsIdentity()));
}