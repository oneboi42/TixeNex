using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class JwtAuthenticationStateProvider : AuthenticationStateProvider
{
    private readonly TokenStorageService _tokenStorage;

    public JwtAuthenticationStateProvider(TokenStorageService tokenStorage)
    {
        _tokenStorage = tokenStorage;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await _tokenStorage.GetTokenAsync();

        if (string.IsNullOrWhiteSpace(token))
        {
            return new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
        }

        var identity = BuildIdentityFromToken(token);
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public void NotifyUserAuthentication(string token)
    {
        var identity = BuildIdentityFromToken(token);
        NotifyAuthenticationStateChanged(Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(identity))));
    }

    public void NotifyUserLogout()
    {
        NotifyAuthenticationStateChanged(Task.FromResult(
            new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
    }

    private static ClaimsIdentity BuildIdentityFromToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        var claims = jwt.Claims.ToList();
        return new ClaimsIdentity(claims, "jwt");
    }
}