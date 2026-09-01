using TixeNex.Api.Domain;

namespace TixeNex.Api.Application.TicketVisibility;

public static class TicketVisibilityQueryExtensions
{
    public static bool IsDemoWorkspace(this Ticket ticket)
    {
        return ticket.DemoExpiresAtUtc is not null ||
               ticket.Origin is TicketOrigin.DemoSeed or TicketOrigin.DemoUser;
    }

    public static IQueryable<Ticket> ApplyWorkspace(
        this IQueryable<Ticket> query,
        TicketVisibilityContext context)
    {
        return query.ApplyWorkspace(context.IsDemoWorkspace);
    }

    public static IQueryable<Ticket> ApplyWorkspace(
        this IQueryable<Ticket> query,
        bool isDemoWorkspace)
    {
        return isDemoWorkspace
            ? query.Where(ticket => ticket.DemoExpiresAtUtc != null || ticket.Origin == TicketOrigin.DemoSeed || ticket.Origin == TicketOrigin.DemoUser)
            : query.Where(ticket => ticket.DemoExpiresAtUtc == null && ticket.Origin == TicketOrigin.Normal);
    }

    public static IQueryable<Ticket> ApplyVisibility(
        this IQueryable<Ticket> query,
        TicketVisibilityContext context)
    {
        query = query.ApplyWorkspace(context);

        return context.Scope switch
        {
            TicketVisibilityScope.Own =>
                query.Where(ticket => ticket.RequesterUserId == context.UserId),
            TicketVisibilityScope.Assigned =>
                query.Where(ticket =>
                    ticket.AssignedToUserId == context.UserId ||
                    ticket.RequesterUserId == context.UserId),
            TicketVisibilityScope.All => query,
            _ => throw new ArgumentOutOfRangeException(nameof(context))
        };
    }
}
