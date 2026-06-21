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

        return CreateAuthenticationState(auth);
    }

    public void NotifyUserAuthentication(AuthResponseDto auth)
    {
        NotifyAuthenticationStateChanged(
            Task.FromResult(CreateAuthenticationState(auth)));
    }

    public void NotifyUserLogout()
    {
        NotifyAuthenticationStateChanged(
            Task.FromResult(Anonymous()));
    }

    private static AuthenticationState CreateAuthenticationState(AuthResponseDto auth)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, auth.UserName),
            new(ClaimTypes.Role, auth.Role)
        };

        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        return new AuthenticationState(user);
    }

    private static AuthenticationState Anonymous()
    {
        return new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity()));
    }
}