using System.Security.Claims;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Audit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "CanViewAudit")]
public sealed class AuditController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuditController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditLogListItemDto>>> Get(
        [FromQuery] string? action,
        [FromQuery] string? entityName,
        [FromQuery] string? performedBy,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken ct)
    {
        var currentUserId =
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(currentUserId))
            return Unauthorized();

        var currentUser = await _db.Users
            .AsNoTracking()
            .Where(user => user.Id == currentUserId)
            .Select(user => new
            {
                user.IsDemoWorkspace
            })
            .SingleOrDefaultAsync(ct);

        if (currentUser is null)
            return Unauthorized();

        var query =
            from audit in _db.AuditLogs.AsNoTracking()
            join actor in _db.Users.AsNoTracking()
                on audit.UserId equals actor.Id
            where actor.IsDemoWorkspace == currentUser.IsDemoWorkspace
            select new
            {
                Audit = audit,
                ActorDisplayName = actor.DisplayName
            };

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(
                item => item.Audit.Action == action);
        }

        if (!string.IsNullOrWhiteSpace(entityName))
        {
            query = query.Where(
                item => item.Audit.EntityName == entityName);
        }

        if (!string.IsNullOrWhiteSpace(performedBy))
        {
            query = query.Where(
                item =>
                    (item.Audit.UserName != null &&
                     item.Audit.UserName.Contains(performedBy)) ||
                    item.ActorDisplayName.Contains(performedBy));
        }

        if (fromUtc.HasValue)
        {
            query = query.Where(
                item => item.Audit.CreatedAtUtc >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(
                item => item.Audit.CreatedAtUtc <= toUtc.Value);
        }

        var rows = await query
            .OrderByDescending(item => item.Audit.CreatedAtUtc)
            .Take(200)
            .Select(item => new AuditLogListItemDto
            {
                CreatedAtUtc = item.Audit.CreatedAtUtc,
                Action = item.Audit.Action,
                EntityName = item.Audit.EntityName,
                EntityId = item.Audit.EntityId,

                PerformedBy = string.IsNullOrWhiteSpace(
                    item.ActorDisplayName)
                        ? item.Audit.UserName ?? string.Empty
                        : item.ActorDisplayName,

                // Do not expose public demo visitors' IP addresses
                // to other demo visitors.
                IpAddress = currentUser.IsDemoWorkspace
                    ? string.Empty
                    : item.Audit.IpAddress ?? string.Empty,

                Details = item.Audit.DetailsJson ?? string.Empty
            })
            .ToListAsync(ct);

        return Ok(rows);
    }
}