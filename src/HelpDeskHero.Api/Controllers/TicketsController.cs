using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class TicketsController : ControllerBase
{
    private static readonly List<TicketDto> Tickets =
    [
        new TicketDto
        {
            Id = 1,
            Number = "HDH-0001",
            Title = "Printer not working",
            Description = "Office printer shows paper jam.",
            Status = "New",
            Priority = "High",
            CreatedAtUtc = DateTime.UtcNow
        },
        new TicketDto
        {
            Id = 2,
            Number = "HDH-0002",
            Title = "VPN access issue",
            Description = "User cannot connect to VPN.",
            Status = "InProgress",
            Priority = "Medium",
            CreatedAtUtc = DateTime.UtcNow
        }
    ];

    [HttpGet]
    public ActionResult<IReadOnlyList<TicketDto>> GetAll()
    {
        return Ok(Tickets.OrderByDescending(x => x.Id).ToList());
    }

    [HttpGet("{id:int}")]
    public ActionResult<TicketDto> GetById(int id)
    {
        var ticket = Tickets.FirstOrDefault(x => x.Id == id);
        return ticket is null ? NotFound() : Ok(ticket);
    }

    [HttpPost]
    public ActionResult<TicketDto> Create(CreateTicketDto dto)
    {
        var nextId = Tickets.Count == 0 ? 1 : Tickets.Max(x => x.Id) + 1;

        var ticket = new TicketDto
        {
            Id = nextId,
            Number = $"HDH-{nextId:0000}",
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority,
            Status = "New",
            CreatedAtUtc = DateTime.UtcNow
        };

        Tickets.Add(ticket);

        return CreatedAtAction(nameof(GetById), new { id = ticket.Id }, ticket);
    }

    [HttpPut("{id:int}")]
    public IActionResult Update(int id, UpdateTicketDto dto)
    {
        var ticket = Tickets.FirstOrDefault(x => x.Id == id);
        if (ticket is null)
            return NotFound();

        ticket.Title = dto.Title;
        ticket.Description = dto.Description;
        ticket.Status = dto.Status;
        ticket.Priority = dto.Priority;

        return NoContent();
    }
}