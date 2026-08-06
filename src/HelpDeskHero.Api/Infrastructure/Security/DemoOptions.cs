namespace HelpDeskHero.Api.Infrastructure.Security;

public sealed class DemoOptions
{
    public const string SectionName = "Demo";

    public bool Enabled { get; set; }

    public int SlidingLifetimeMinutes { get; set; } = 60;

    public int AbsoluteLifetimeMinutes { get; set; } = 180;

    public int MaxActiveUsers { get; set; } = 50;

    public string[] AllowedRoles { get; set; } =
    [
        "User",
        "Agent",
        "Admin"
    ];
}