using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Application.Services;

public sealed class SlaCalculator : ISlaCalculator
{
    private readonly AppDbContext _db;

    public SlaCalculator(AppDbContext db)
    {
        _db = db;
    }

    public async Task ApplySlaAsync(Ticket ticket, CancellationToken ct = default)
    {
        var policy = await _db.TicketSlaPolicies
            .AsNoTracking()
            .Where(x => x.IsActive && x.Priority == ticket.Priority)
            .OrderBy(x => x.Id)
            .FirstOrDefaultAsync(ct);

        if (policy is null)
            return;

        var createdAtUtc = ticket.CreatedAtUtc == default
            ? DateTime.UtcNow
            : ticket.CreatedAtUtc;

        ticket.DueFirstResponseAtUtc = createdAtUtc.AddMinutes(policy.FirstResponseMinutes);
        ticket.DueResolveAtUtc = createdAtUtc.AddMinutes(policy.ResolveMinutes);
    }
}
