namespace TixeNex.Api.Application.TicketVisibility;

public enum TicketVisibilityScope
{
    Own,
    Assigned,
    All
}

public sealed record TicketVisibilityContext(
    string UserId,
    TicketVisibilityScope Scope,
    bool IsDemoWorkspace);

public enum TicketVisibilityResolutionStatus
{
    Resolved,
    Unauthorized,
    Forbidden
}

public sealed record TicketVisibilityResolution(
    TicketVisibilityResolutionStatus Status,
    TicketVisibilityContext? Context = null);
