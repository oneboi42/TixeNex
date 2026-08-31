using TixeNex.Api.Domain;

namespace TixeNex.Api.Infrastructure.Persistence;

internal static class DemoSeedTicketCatalog
{
    private static readonly DemoSeedTicketDefinition[] Definitions =
    [
        new(1, "Medium", "InProgress", "demo-agent-1", null),
        new(2, "High", "Resolved", "demo-agent-1", null),
        new(3, "Critical", "Closed", "demo-agent-1", "demo-agent-1"),
        new(4, "Low", "New", "demo-agent-2", "demo-agent-2"),
        new(5, "Medium", "InProgress", "demo-agent-2", "demo-agent-2"),
        new(6, "High", "Resolved", "demo-agent-2", "demo-agent-2")
    ];

    public static IReadOnlyCollection<string> UserNames { get; } =
        Definitions
            .SelectMany(definition =>
                new[]
                {
                    definition.RequesterUserName,
                    definition.AssignedToUserName
                })
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public static IReadOnlyList<Ticket> CreateTickets(
        DateTime now,
        IReadOnlyDictionary<string, string> userIdsByName)
    {
        return Definitions
            .Select(definition =>
            {
                var numberSuffix = Guid.NewGuid()
                    .ToString("N")[..6]
                    .ToUpperInvariant();

                var ticket = new Ticket
                {
                    Number = $"HDH-SEED-{now:yyyyMMdd}-{definition.Index:D2}-{numberSuffix}",
                    CreatedAtUtc = now.AddDays(-definition.Index)
                };

                Restore(ticket, definition, userIdsByName);
                return ticket;
            })
            .ToArray();
    }

    public static bool TryRestore(
        Ticket ticket,
        IReadOnlyDictionary<string, string> userIdsByName)
    {
        if (!TryGetDefinitionIndex(ticket.Number, out var definitionIndex))
            return false;

        var definition = Definitions
            .SingleOrDefault(candidate => candidate.Index == definitionIndex);

        if (definition is null)
            return false;

        Restore(ticket, definition, userIdsByName);
        return true;
    }

    private static bool TryGetDefinitionIndex(
        string ticketNumber,
        out int definitionIndex)
    {
        definitionIndex = 0;
        var parts = ticketNumber.Split('-');

        return parts.Length == 5 &&
               string.Equals(parts[0], "HDH", StringComparison.OrdinalIgnoreCase) &&
               string.Equals(parts[1], "SEED", StringComparison.OrdinalIgnoreCase) &&
               int.TryParse(parts[3], out definitionIndex);
    }

    private static void Restore(
        Ticket ticket,
        DemoSeedTicketDefinition definition,
        IReadOnlyDictionary<string, string> userIdsByName)
    {
        ticket.Title = $"Sample Seeded Ticket {definition.Index}";
        ticket.Description = "This is a seeded demo ticket for exploration.";
        ticket.Status = definition.Status;
        ticket.Priority = definition.Priority;
        ticket.Origin = TicketOrigin.DemoSeed;
        ticket.UpdatedAtUtc = null;
        ticket.DueFirstResponseAtUtc = null;
        ticket.DueResolveAtUtc = null;
        ticket.FirstRespondedAtUtc = null;
        ticket.ResolvedAtUtc = null;
        ticket.RequesterUserId = userIdsByName[definition.RequesterUserName];
        ticket.AssignedToUserId = definition.AssignedToUserName is null
            ? null
            : userIdsByName[definition.AssignedToUserName];
        ticket.DemoExpiresAtUtc = null;
        ticket.EscalationLevel = 0;
        ticket.LastNotifiedAtUtc = null;
        ticket.IsDeleted = false;
        ticket.DeletedAtUtc = null;
        ticket.DeletedByUserId = null;
    }

    private sealed record DemoSeedTicketDefinition(
        int Index,
        string Priority,
        string Status,
        string RequesterUserName,
        string? AssignedToUserName);
}
