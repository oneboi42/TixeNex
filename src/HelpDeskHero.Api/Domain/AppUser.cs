namespace HelpDeskHero.Api.Domain;

public sealed class AppUser
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string Role { get; set; } = "User";

    public List<RefreshToken> RefreshTokens { get; set; } = [];
}