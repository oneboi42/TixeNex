namespace HelpDeskHero.Api.Domain;

public sealed class RefreshToken
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public string Token { get; set; } = string.Empty;
    public string TokenHash { get; set; } = string.Empty;

    public string? ReplacedByToken { get; set; }
    
    public string DeviceName { get; set; } = string.Empty;
    public string? IpAddress { get; set; }

    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }

    public bool IsActive => RevokedAtUtc is null && ExpiresAtUtc > DateTime.UtcNow;

    public AppUser? User { get; set; }
}