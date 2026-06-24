using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Services;
using HelpDeskHero.Shared.Contracts.Common;
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
    private readonly AuditService _audit;

    public TicketsController(AppDbContext db, AuditService audit)
    {
        _db = db;
        _audit = audit;
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

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TicketDto>> GetById(int id, CancellationToken ct)
    {
        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return NotFound();

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

        _db.Tickets.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("Create", "Ticket", entity.Id.ToString(), new { entity.Number, entity.Title }, ct);

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
            return NotFound();

        var originalRowVersion = Convert.FromBase64String(dto.RowVersionBase64);
        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = originalRowVersion;

        entity.Title = dto.Title.Trim();
        entity.Description = dto.Description.Trim();
        entity.Status = dto.Status;
        entity.Priority = dto.Priority;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        await _audit.WriteAsync("Update", "Ticket", entity.Id.ToString(), new { entity.Number, entity.Title }, ct);

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> SoftDelete(int id, CancellationToken ct)
    {
        var entity = await _db.Tickets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return NotFound();

        entity.IsDeleted = true;
        entity.DeletedAtUtc = DateTime.UtcNow;
        entity.DeletedByUserId = User.Identity?.Name;
        entity.UpdatedAtUtc = DateTime.UtcNow;

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

    private static BadRequestObjectResult ValidationError(Dictionary<string, string[]> errors)
    {
        return new BadRequestObjectResult(new
        {
            code = "validation_error",
            errors
        });
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
}