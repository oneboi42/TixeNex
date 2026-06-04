using HelpDeskHero.Api.Domain;
using System.Security.Claims;

namespace HelpDeskHero.Api.Infrastructure.Security;

public interface ITokenService
{
    string CreateAccessToken(AppUser user, DateTime expiresAtUtc);
    RefreshToken CreateRefreshToken(DateTime expiresAtUtc);
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}