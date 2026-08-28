using TixeNex.Api.Application.Interfaces;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Shared.Contracts.Tickets;
using Microsoft.EntityFrameworkCore;

namespace TixeNex.Api.Application.Services;

public sealed class SlaMonitorService : ISlaMonitorService
{
    private readonly AppDbContext _db;
    private readonly IOutboxWriter _outboxWriter;

    public SlaMonitorService(AppDbContext db, IOutboxWriter outboxWriter)
    {
        _db = db;
        _outboxWriter = outboxWriter;
    }

    public async Task CheckBreachesAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var notificationThreshold = now.AddMinutes(-30);

        var breachedTickets = await _db.Tickets
            .Where(x => !x.IsDeleted)
            .Where(x => x.Status == "New" || x.Status == "InProgress")
            .Where(x => x.DueResolveAtUtc != null && x.DueResolveAtUtc < now)
            .Where(x => x.LastNotifiedAtUtc == null || x.LastNotifiedAtUtc < notificationThreshold)
            .ToListAsync(ct);

        foreach (var ticket in breachedTickets)
        {
            ticket.EscalationLevel += 1;
            ticket.LastNotifiedAtUtc = now;

            _db.TicketEscalations.Add(new TicketEscalation
            {
                TicketId = ticket.Id,
                EscalationLevel = ticket.EscalationLevel,
                TriggeredAtUtc = now,
                Reason = "Resolve SLA breached.",
                AssignedToUserId = ticket.AssignedToUserId,
                NotificationSent = false
            });

            await _outboxWriter.AddAsync("TicketChanged", new TicketLiveUpdateDto
            {
                TicketId = ticket.Id,
                EventType = "SlaBreached",
                Status = ticket.Status,
                Priority = ticket.Priority,
                AssignedToUserId = ticket.AssignedToUserId,
                EscalationLevel = ticket.EscalationLevel,
                ChangedAtUtc = now
            }, ct);
        }

        await _db.SaveChangesAsync(ct);
    }
}
