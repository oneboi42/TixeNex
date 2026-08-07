using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HelpDeskHero.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace HelpDeskHero.Api.Infrastructure.Services;

public sealed class TokenService
{
    private readonly IConfiguration _configuration;
    private readonly UserManager<ApplicationUser> _userManager;

    public TokenService(IConfiguration configuration, UserManager<ApplicationUser> userManager)
    {
        _configuration = configuration;
        _userManager = userManager;
    }

    public async Task<(string token, DateTime expiresAtUtc)>
        CreateAccessTokenAsync(
            ApplicationUser user,
            DateTime? notAfterUtc = null)
    {
        var jwt = _configuration.GetSection("Jwt");

        var issuer = jwt["Issuer"]!;
        var audience = jwt["Audience"]!;
        var key = jwt["Key"]!;

        var minutes = int.Parse(
            jwt["AccessTokenMinutes"] ?? "15");

        var roles = await _userManager.GetRolesAsync(user);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(
                JwtRegisteredClaimNames.UniqueName,
                user.UserName ?? string.Empty),
            new("display_name", user.DisplayName),
            new(
                "is_demo_workspace",
                user.IsDemoWorkspace ? "true" : "false"),
            new(
                "is_demo",
                user.IsDemoUser ? "true" : "false"),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(
                ClaimTypes.Name,
                user.UserName ?? string.Empty)
        };

        claims.AddRange(
            roles.Select(
                role => new Claim(ClaimTypes.Role, role)));

        var now = DateTime.UtcNow;
        var expiresAtUtc = now.AddMinutes(minutes);

        if (notAfterUtc.HasValue &&
            notAfterUtc.Value < expiresAtUtc)
        {
            expiresAtUtc = notAfterUtc.Value;
        }

        if (expiresAtUtc <= now)
        {
            throw new InvalidOperationException(
                "Cannot create an access token for an expired session.");
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: credentials);

        var tokenValue =
            new JwtSecurityTokenHandler().WriteToken(token);

        return (tokenValue, expiresAtUtc);
    }
}
