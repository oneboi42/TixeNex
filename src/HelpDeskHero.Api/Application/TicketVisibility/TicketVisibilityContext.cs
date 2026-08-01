namespace HelpDeskHero.Api.Application.TicketVisibility;

public enum TicketVisibilityScope
{
    Own,
    Assigned,
    All
}

public sealed record TicketVisibilityContext(
    string UserId,
    TicketVisibilityScope Scope);

public enum TicketVisibilityResolutionStatus
{
    Resolved,
    Unauthorized,
    Forbidden
}

public sealed record TicketVisibilityResolution(
    TicketVisibilityResolutionStatus Status,
    TicketVisibilityContext? Context = null);
