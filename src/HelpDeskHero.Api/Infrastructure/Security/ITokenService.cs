using HelpDeskHero.Api.Domain;
using System.Security.Claims;

namespace HelpDeskHero.Api.Infrastructure.Security;

public interface ITokenService
{
    Task<(string Token, DateTime ExpiresAtUtc)> CreateAccessTokenAsync(ApplicationUser user);
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
