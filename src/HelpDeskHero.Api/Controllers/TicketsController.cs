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
        var tickets = await _db.Tickets
            .AsNoTracking()
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

        return Ok(tickets);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TicketDetailsDto>> GetById(int id, CancellationToken ct)
    {
        var ticket = await _db.Tickets
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new TicketDetailsDto
            {
                Id = x.Id,
                Number = x.Number,
                Title = x.Title,
                Description = x.Description,
                Status = x.Status,
                Priority = x.Priority,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .FirstOrDefaultAsync(ct);

        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost]
    public async Task<ActionResult<TicketDetailsDto>> Create(CreateTicketDto dto, CancellationToken ct)
    {
        var nextId = await _db.Tickets.CountAsync(ct) + 1; // Simple way to generate a ticket number, in real app consider using a more robust approach

        var entity = new Ticket
        {
            Number = $"HDH-{nextId:0000}",
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority,
            Status = "New",
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.Tickets.Add(entity);
        await _db.SaveChangesAsync(ct);

        var result = new TicketDetailsDto
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
    public async Task<IActionResult> Update(int id, UpdateTicketDto dto, CancellationToken ct)
    {
        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return NotFound();

        entity.Title = dto.Title;
        entity.Description = dto.Description;
        entity.Status = dto.Status;
        entity.Priority = dto.Priority;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return NotFound();

        _db.Tickets.Remove(entity);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}