using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class TicketsController : ControllerBase
{
    private readonly AppDbContext _db;

    public TicketsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketDto>>> GetAll(CancellationToken ct)
    {
        var result = await _db.Tickets
            .OrderByDescending(x => x.Id)
            .Select(x => new TicketDto
            {
                Id = x.Id,
                Number = x.Number,
                Title = x.Title,
                Description = x.Description,
                Status = x.Status,
                Priority = x.Priority,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TicketDto>> GetById(int id, CancellationToken ct)
    {
        var ticket = await _db.Tickets
            .Where(x => x.Id == id)
            .Select(x => new TicketDto
            {
                Id = x.Id,
                Number = x.Number,
                Title = x.Title,
                Description = x.Description,
                Status = x.Status,
                Priority = x.Priority,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .SingleOrDefaultAsync(ct);

        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<ActionResult<TicketDto>> Create(CreateTicketDto dto, CancellationToken ct)
    {
        var nextNumber = $"HDH-{(await _db.Tickets.CountAsync(ct) + 1):0000}";

        var entity = new Ticket
        {
            Number = nextNumber,
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority,
            Status = "New",
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Tickets.Add(entity);
        await _db.SaveChangesAsync(ct);

        var result = new TicketDto
        {
            Id = entity.Id,
            Number = entity.Number,
            Title = entity.Title,
            Description = entity.Description,
            Status = entity.Status,
            Priority = entity.Priority,
            CreatedAtUtc = entity.CreatedAtUtc
        };

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "AgentOrAdmin")]
    public async Task<IActionResult> Update(int id, UpdateTicketDto dto, CancellationToken ct)
    {
        var ticket = await _db.Tickets.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (ticket is null)
            return NotFound();

        ticket.Title = dto.Title;
        ticket.Description = dto.Description;
        ticket.Status = dto.Status;
        ticket.Priority = dto.Priority;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var ticket = await _db.Tickets.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (ticket is null)
            return NotFound();

        _db.Tickets.Remove(ticket);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}