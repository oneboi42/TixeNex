namespace HelpDeskHero.Shared.Contracts.Auth;

public sealed class TokenResponseDto
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTime AccessTokenExpiresAtUtc { get; set; }

    public string RefreshToken { get; set; } = string.Empty;
    public DateTime RefreshTokenExpiresAtUtc { get; set; }

    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string[] Roles { get; set; } = [];

    public bool IsDemoUser { get; set; }
    public DateTime? DemoExpiresAtUtc { get; set; }
    public DateTime? DemoAbsoluteExpiresAtUtc { get; set; }
}