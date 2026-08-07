using System.Security.Claims;

namespace HelpDeskHero.Api.Infrastructure.Security;

public static class DemoWorkspaceClaims
{
    public static bool IsDemoWorkspace(ClaimsPrincipal principal)
    {
        var workspaceClaim = principal.FindFirst("is_demo_workspace")?.Value;

        return workspaceClaim is null
            ? IsTrue(principal.FindFirst("is_demo")?.Value)
            : IsTrue(workspaceClaim);
    }

    private static bool IsTrue(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
