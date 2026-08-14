using System.Security.Claims;
using HelpDeskHero.Api.Infrastructure.Security;

namespace HelpDeskHero.Api.Application.TicketVisibility;

public sealed class TicketVisibilityContextResolver : ITicketVisibilityContextResolver
{
    public TicketVisibilityResolution Resolve(ClaimsPrincipal principal)
    {
        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            return new TicketVisibilityResolution(
                TicketVisibilityResolutionStatus.Unauthorized);
        }

        var scope = principal.IsInRole("Admin")
            ? TicketVisibilityScope.All
            : principal.IsInRole("Agent")
                ? TicketVisibilityScope.Assigned
                : principal.IsInRole("User")
                    ? TicketVisibilityScope.Own
                    : (TicketVisibilityScope?)null;

        if (scope is null)
        {
            return new TicketVisibilityResolution(
                TicketVisibilityResolutionStatus.Forbidden);
        }

        return new TicketVisibilityResolution(
            TicketVisibilityResolutionStatus.Resolved,
            new TicketVisibilityContext(
                userId,
                scope.Value,
                DemoWorkspaceClaims.IsDemoWorkspace(principal)));
    }
}
