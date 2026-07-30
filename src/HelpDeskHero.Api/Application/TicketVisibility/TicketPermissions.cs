using HelpDeskHero.Api.Domain;

namespace HelpDeskHero.Api.Application.TicketVisibility;

public static class TicketPermissions
{
    public static bool CanEdit(Ticket ticket, TicketVisibilityContext context) =>
        context.Scope == TicketVisibilityScope.All ||
        context.Scope == TicketVisibilityScope.Assigned &&
        ticket.AssignedToUserId == context.UserId;

    public static bool CanStart(Ticket ticket, TicketVisibilityContext context) =>
        ticket.Status == "New" && CanWork(ticket, context);

    public static bool CanResolve(Ticket ticket, TicketVisibilityContext context) =>
        ticket.Status == "InProgress" && CanWork(ticket, context);

    public static bool CanClose(Ticket ticket, TicketVisibilityContext context) =>
        ticket.Status == "Resolved" && CanManageAsRequester(ticket, context);

    public static bool CanReopen(Ticket ticket, TicketVisibilityContext context) =>
        ticket.Status == "Resolved" && CanManageAsRequester(ticket, context);

    public static bool CanWork(Ticket ticket, TicketVisibilityContext context) =>
        context.Scope == TicketVisibilityScope.All ||
        context.Scope == TicketVisibilityScope.Assigned &&
        ticket.AssignedToUserId == context.UserId;

    public static bool CanManageAsRequester(Ticket ticket, TicketVisibilityContext context) =>
        context.Scope == TicketVisibilityScope.All ||
        ticket.RequesterUserId == context.UserId;
}
