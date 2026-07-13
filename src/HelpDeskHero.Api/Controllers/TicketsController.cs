using Hangfire;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.BackgroundJobs.Contracts;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Services;
using HelpDeskHero.Shared.Contracts.Common;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class TicketsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ISlaCalculator _slaCalculator;
    private readonly ITicketAssignmentService _ticketAssignmentService;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IWebHostEnvironment _environment;

    public TicketsController(
        AppDbContext db,
        AuditService audit,
        ISlaCalculator slaCalculator,
        ITicketAssignmentService ticketAssignmentService,
        IOutboxWriter outboxWriter,
        IWebHostEnvironment environment)
    {
        _db = db;
        _audit = audit;
        _slaCalculator = slaCalculator;
        _ticketAssignmentService = ticketAssignmentService;
        _outboxWriter = outboxWriter;
        _environment = environment;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<TicketDto>>> GetAll([FromQuery] TicketQueryDto query, CancellationToken ct)
    {
        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;
        pageSize = pageSize > 100 ? 100 : pageSize;

        var q = _db.Tickets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();

            q = q.Where(x =>
                x.Number.Contains(search) ||
                x.Title.Contains(search) ||
                x.Description.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            q = q.Where(x => x.Status == query.Status);
        }

        if (!string.IsNullOrWhiteSpace(query.Priority))
        {
            q = q.Where(x => x.Priority == query.Priority);
        }

        q = query.SortBy switch
        {
            "Title" => query.Desc ? q.OrderByDescending(x => x.Title) : q.OrderBy(x => x.Title),
            "Priority" => query.Desc ? q.OrderByDescending(x => x.Priority) : q.OrderBy(x => x.Priority),
            "Status" => query.Desc ? q.OrderByDescending(x => x.Status) : q.OrderBy(x => x.Status),
            _ => query.Desc ? q.OrderByDescending(x => x.CreatedAtUtc) : q.OrderBy(x => x.CreatedAtUtc)
        };

        var totalCount = await q.CountAsync(ct);

        var items = await q
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new TicketDto
            {
                Id = x.Id,
                Number = x.Number,
                Title = x.Title,
                Description = x.Description,
                Status = x.Status,
                Priority = x.Priority,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc,
                RowVersionBase64 = Convert.ToBase64String(x.RowVersion)
            })
            .ToListAsync(ct);

        return Ok(new PagedResultDto<TicketDto>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = items
        });
    }

    [HttpGet("export")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] string? status,
        [FromQuery] string? priority,
        CancellationToken ct)
    {
        var query = _db.Tickets.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
            query = query.Where(x => x.Status == status);

        if (!string.IsNullOrWhiteSpace(priority))
            query = query.Where(x => x.Priority == priority);

        var rows = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Number,
                x.Title,
                x.Status,
                x.Priority,
                x.CreatedAtUtc
            })
            .ToListAsync(ct);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Id,Number,Title,Status,Priority,CreatedAtUtc");

        foreach (var row in rows)
        {
            var title = row.Title.Replace("\"", "\"\"");
            sb.AppendLine($"{row.Id},{row.Number},\"{title}\",{row.Status},{row.Priority},{row.CreatedAtUtc:O}");
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", $"tickets-{DateTime.UtcNow:yyyyMMddHHmmss}.csv");
    }

    [HttpGet("deleted")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<ActionResult<List<TicketDto>>> GetDeleted(CancellationToken ct)
    {
        var items = await _db.Tickets
            .IgnoreQueryFilters()
            .Where(x => x.IsDeleted)
            .OrderByDescending(x => x.DeletedAtUtc ?? x.CreatedAtUtc)
            .Select(x => new TicketDto
            {
                Id = x.Id,
                Number = x.Number,
                Title = x.Title,
                Description = x.Description,
                Status = x.Status,
                Priority = x.Priority,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc,
                RowVersionBase64 = Convert.ToBase64String(x.RowVersion)
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost("{id:int}/restore")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> Restore(int id, CancellationToken ct)
    {
        var ticket = await _db.Tickets
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (ticket is null)
            return TicketNotFound(id);

        if (!ticket.IsDeleted)
        {
            return BusinessProblem(
                StatusCodes.Status400BadRequest,
                "Ticket nie znajduje się w koszu",
                "Nie można przywrócić zgłoszenia, które nie zostało usunięte.");
        }

        ticket.IsDeleted = false;
        ticket.DeletedAtUtc = null;
        ticket.DeletedByUserId = null;
        ticket.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("Restore", "Ticket", ticket.Id.ToString(), new { ticket.Number, ticket.Title }, ct);

        return NoContent();
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TicketDto>> GetById(int id, CancellationToken ct)
    {
        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
            return TicketNotFound(id);

        return Ok(ToDto(entity));
    }

    [HttpPost]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<ActionResult<TicketDto>> Create(CreateTicketDto dto, CancellationToken ct)
    {
        var errors = ValidateCreate(dto);

        if (errors.Count > 0)
            return ValidationError(errors);

        var nextNumber = $"HDH-{DateTime.UtcNow:yyyyMMddHHmmss}";

        var entity = new Ticket
        {
            Number = nextNumber,
            Title = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            Priority = dto.Priority,
            Status = "New",
            CreatedAtUtc = DateTime.UtcNow
        };

        await _slaCalculator.ApplySlaAsync(entity, ct);
        await _ticketAssignmentService.AssignAsync(entity, ct);

        _db.Tickets.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _outboxWriter.AddAsync("TicketChanged", ToLiveUpdateDto(entity, "Created"), ct);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("Create", "Ticket", entity.Id.ToString(), new { entity.Number, entity.Title }, ct);

        if (!_environment.IsEnvironment("Testing"))
        {
            BackgroundJob.Enqueue<INotificationJob>(job =>
                job.SendTicketCreatedNotificationsAsync(entity.Id, default));
        }

        var result = ToDto(entity);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> Update(int id, UpdateTicketDto dto, CancellationToken ct)
    {
        var errors = ValidateUpdate(dto);

        if (errors.Count > 0)
            return ValidationError(errors);

        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
            return TicketNotFound(id);

        var originalPriority = entity.Priority;

        var originalRowVersion = Convert.FromBase64String(dto.RowVersionBase64);
        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = originalRowVersion;

        entity.Title = dto.Title.Trim();
        entity.Description = dto.Description.Trim();
        entity.Status = dto.Status;
        entity.Priority = dto.Priority;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        if (entity.Status == "Closed" && entity.ResolvedAtUtc is null)
        {
            entity.ResolvedAtUtc = DateTime.UtcNow;
        }

        if (originalPriority != entity.Priority)
        {
            await _slaCalculator.ApplySlaAsync(entity, ct);
        }

        await _outboxWriter.AddAsync("TicketChanged", ToLiveUpdateDto(entity, "Updated"), ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConflictProblem();
        }

        await _audit.WriteAsync("Update", "Ticket", entity.Id.ToString(), new { entity.Number, entity.Title }, ct);

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> SoftDelete(int id, CancellationToken ct)
    {
        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
            return TicketNotFound(id);

        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = User.Identity?.Name;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _outboxWriter.AddAsync("TicketChanged", ToLiveUpdateDto(entity, "Deleted"), ct);

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("SoftDelete", "Ticket", entity.Id.ToString(), new { entity.Number, entity.Title }, ct);

        return NoContent();
    }

    private static readonly string[] AllowedPriorities =
    [
        "Low",
        "Medium",
        "High",
        "Critical"
    ];

    private static readonly string[] AllowedStatuses =
    [
        "New",
        "InProgress",
        "Resolved",
        "Closed"
    ];

    private BadRequestObjectResult ValidationError(Dictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails(errors)
        {
            Title = "Nieprawidłowe dane zgłoszenia",
            Detail = "Popraw błędy walidacji i spróbuj ponownie.",
            Status = StatusCodes.Status400BadRequest,
            Type = "https://httpstatuses.com/400"
        };

        details.Extensions["code"] = "validation_error";

        return BadRequest(details);
    }

    private ObjectResult TicketNotFound(int id)
    {
        return BusinessProblem(
            StatusCodes.Status404NotFound,
            "Nie znaleziono zgłoszenia",
            $"Zgłoszenie o ID {id} nie istnieje albo zostało usunięte.",
            "ticket_not_found");
    }

    private ObjectResult ConflictProblem()
    {
        return BusinessProblem(
            StatusCodes.Status409Conflict,
            "Konflikt danych",
            "Zgłoszenie zostało zmienione przez innego użytkownika. Odśwież widok i spróbuj ponownie.",
            "concurrency_conflict");
    }

    private ObjectResult BusinessProblem(
        int statusCode,
        string title,
        string detail,
        string code = "business_error")
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type = $"https://httpstatuses.com/{statusCode}",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = code;

        return StatusCode(statusCode, problem);
    }

    private static Dictionary<string, string[]> ValidateCreate(CreateTicketDto dto)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(dto.Title))
        {
            errors["Title"] = ["Title is required."];
        }
        else if (dto.Title.Trim().Length < 3 || dto.Title.Trim().Length > 200)
        {
            errors["Title"] = ["Title must be between 3 and 200 characters."];
        }

        if (string.IsNullOrWhiteSpace(dto.Description))
        {
            errors["Description"] = ["Description is required."];
        }
        else if (dto.Description.Trim().Length < 5 || dto.Description.Trim().Length > 4000)
        {
            errors["Description"] = ["Description must be between 5 and 4000 characters."];
        }

        if (string.IsNullOrWhiteSpace(dto.Priority))
        {
            errors["Priority"] = ["Priority is required."];
        }
        else if (!AllowedPriorities.Contains(dto.Priority))
        {
            errors["Priority"] = ["Priority must be one of: Low, Medium, High, Critical."];
        }

        return errors;
    }

    private static Dictionary<string, string[]> ValidateUpdate(UpdateTicketDto dto)
    {
        var errors = ValidateCreate(new CreateTicketDto
        {
            Title = dto.Title,
            Description = dto.Description,
            Priority = dto.Priority
        });

        if (string.IsNullOrWhiteSpace(dto.Status))
        {
            errors["Status"] = ["Status is required."];
        }
        else if (!AllowedStatuses.Contains(dto.Status))
        {
            errors["Status"] = ["Status must be one of: New, InProgress, Resolved, Closed."];
        }

        if (string.IsNullOrWhiteSpace(dto.RowVersionBase64))
        {
            errors["RowVersionBase64"] = ["RowVersion is required."];
        }
        else
        {
            try
            {
                Convert.FromBase64String(dto.RowVersionBase64);
            }
            catch
            {
                errors["RowVersionBase64"] = ["RowVersion has invalid format."];
            }
        }

        return errors;
    }

    private static TicketDto ToDto(Ticket entity) => new()
    {
        Id = entity.Id,
        Number = entity.Number,
        Title = entity.Title,
        Description = entity.Description,
        Status = entity.Status,
        Priority = entity.Priority,
        CreatedAtUtc = entity.CreatedAtUtc,
        UpdatedAtUtc = entity.UpdatedAtUtc,
        RowVersionBase64 = Convert.ToBase64String(entity.RowVersion)
    };

    private static TicketLiveUpdateDto ToLiveUpdateDto(Ticket entity, string eventType) => new()
    {
        TicketId = entity.Id,
        EventType = eventType,
        Status = entity.Status,
        Priority = entity.Priority,
        AssignedToUserId = entity.AssignedToUserId,
        EscalationLevel = entity.EscalationLevel,
        ChangedAtUtc = DateTime.UtcNow
    };
}
