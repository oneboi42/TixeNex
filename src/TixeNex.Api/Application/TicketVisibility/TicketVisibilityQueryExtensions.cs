using TixeNex.Api.Domain;

namespace TixeNex.Api.Application.TicketVisibility;

public static class TicketVisibilityQueryExtensions
{
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
            ? query.Where(ticket => ticket.DemoExpiresAtUtc != null)
            : query.Where(ticket => ticket.DemoExpiresAtUtc == null);
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
