using System.Net.Http.Headers;
using HelpDeskHero.UI.Services.Auth;

namespace HelpDeskHero.UI.Handlers;

public sealed class AuthHttpMessageHandler : DelegatingHandler
{
    private readonly AuthSessionService _authSessionService;

    public AuthHttpMessageHandler(AuthSessionService authSessionService)
    {
        _authSessionService = authSessionService;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authSessionService.GetValidAccessTokenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return await base.SendAsync(request, cancellationToken);
    }
}