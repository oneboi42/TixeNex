using System.Net.Http.Headers;

namespace HelpDeskHero.UI.Services.Auth;

public sealed class AuthTokenHandler : DelegatingHandler
{
    private readonly AuthSessionService _session;

    public AuthTokenHandler(AuthSessionService session)
    {
        _session = session;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _session.GetValidAccessTokenAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}