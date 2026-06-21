using Microsoft.AspNetCore.Identity;

namespace HelpDeskHero.Api.Domain;

public sealed class AppUser : IdentityUser<int>
{
    public string Role { get; set; } = "User";

    public List<RefreshToken> RefreshTokens { get; set; } = [];
}