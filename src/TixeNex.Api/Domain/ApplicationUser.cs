using Microsoft.AspNetCore.Identity;

namespace TixeNex.Api.Domain;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public bool IsDemoWorkspace { get; set; }
    public bool IsDemoUser { get; set; }
    public DateTime? DemoExpiresAtUtc { get; set; }
    public DateTime? DemoAbsoluteExpiresAtUtc { get; set; }
    public DateTime? LastActivityAtUtc { get; set; }
}
