using HelpDeskHero.Api.Domain;

namespace HelpDeskHero.Api.Application.TicketVisibility;

public static class TicketVisibilityQueryExtensions
{
    public static IQueryable<Ticket> ApplyVisibility(
        this IQueryable<Ticket> query,
        TicketVisibilityContext context)
    {
        return context.Scope switch
        {
            TicketVisibilityScope.Own =>
                query.Where(ticket => ticket.RequesterUserId == context.UserId),
            TicketVisibilityScope.Assigned =>
                query.Where(ticket => ticket.AssignedToUserId == context.UserId),
            TicketVisibilityScope.All => query,
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }
}
