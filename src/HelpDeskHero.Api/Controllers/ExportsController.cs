using System.Security.Claims;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Application.Services.Exports;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Exports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExportsController : ControllerBase
{
    private const int MaxActiveExportsPerUser = 3;

    private readonly AppDbContext _db;
    private readonly IMessagePublisher _publisher;
    private readonly IExportObjectStorage _exportObjectStorage;

    public ExportsController(
        AppDbContext db,
        IMessagePublisher publisher,
        IExportObjectStorage exportObjectStorage)
    {
        _db = db;
        _publisher = publisher;
        _exportObjectStorage = exportObjectStorage;
    }

    [HttpPost]
    public async Task<ActionResult<CreateExportResponseDto>> CreateExport(
        CreateExportRequestDto request,
        CancellationToken cancellationToken)
    {
        var accessError = ResolveCaller(out var userId, out var callerRole);

        if (accessError is not null)
            return accessError;

        var errors = ValidateRequest(
            request,
            out var resourceType,
            out var format,
            out var scope);

        if (errors.Count > 0)
            return ValidationError(errors);

        if (!IsScopeAllowed(callerRole, scope))
            return Forbid();

        var activeExportCount = await _db.ExportJobs.CountAsync(
            exportJob =>
                exportJob.UserId == userId &&
                (exportJob.Status == ExportStatus.Pending ||
                 exportJob.Status == ExportStatus.Running),
            cancellationToken);

        if (activeExportCount >= MaxActiveExportsPerUser)
            return ActiveExportLimitReached();

        var job = new ExportJob
        {
            Id = Guid.NewGuid(),
            UserId = userId!,
            Status = ExportStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ResourceType = resourceType,
            Format = format,
            Scope = scope
        };

        _db.ExportJobs.Add(job);

        await _db.SaveChangesAsync(cancellationToken);

        await _publisher.PublishAsync(
            new ExportRequested(job.Id, job.UserId));

        var response = new CreateExportResponseDto
        {
            Id = job.Id,
            Status = job.Status.ToString()
        };

        return Accepted(
            $"/api/exports/{job.Id}",
            response);
    }

    [HttpGet("options")]
    public ActionResult<ExportOptionsDto> GetOptions()
    {
        var accessError = ResolveCaller(out _, out var callerRole);

        if (accessError is not null)
            return accessError;

        var scopes = callerRole switch
        {
            ExportCallerRole.Admin => new[] { ExportScope.All.ToString() },
            ExportCallerRole.Agent =>
                [ExportScope.Own.ToString(), ExportScope.Assigned.ToString()],
            _ => new[] { ExportScope.Own.ToString() }
        };

        return Ok(new ExportOptionsDto
        {
            AllowedResourceTypes = [ExportResourceType.Tickets.ToString()],
            AllowedFormats = [ExportFormat.Csv.ToString()],
            AllowedScopes = scopes
        });
    }

    [HttpGet]
    public async Task<ActionResult<List<ExportJobDto>>> GetMyExports(
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is null)
        {
            return Unauthorized();
        }

        var exports = await _db.ExportJobs
            .AsNoTracking()
            .Where(exportJob => exportJob.UserId == userId)
            .OrderByDescending(exportJob => exportJob.CreatedAt)
            .Select(exportJob => new ExportJobDto
            {
                Id = exportJob.Id,
                Status = exportJob.Status.ToString(),
                ResourceType = exportJob.ResourceType.ToString(),
                Format = exportJob.Format.ToString(),
                Scope = exportJob.Scope.ToString(),
                FileName = exportJob.FileName,
                ErrorMessage = exportJob.ErrorMessage,
                CreatedAt = exportJob.CreatedAt,
                CompletedAt = exportJob.CompletedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(exports);
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> DownloadExport(
        Guid id,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is null)
        {
            return Unauthorized();
        }

        var job = await _db.ExportJobs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                exportJob =>
                    exportJob.Id == id &&
                    exportJob.UserId == userId,
                cancellationToken);

        if (job is null)
        {
            return NotFound();
        }

        if (job.Status != ExportStatus.Completed)
        {
            return Conflict(new
            {
                message = "The export has not been completed yet."
            });
        }

        if (string.IsNullOrWhiteSpace(job.StorageObjectName) ||
            string.IsNullOrWhiteSpace(job.FileName))
        {
            return Conflict(new
            {
                message = "The export file is not available in object storage."
            });
        }

        var content = await _exportObjectStorage.DownloadAsync(
            job.StorageObjectName,
            cancellationToken);

        return File(
            fileContents: content,
            contentType: "text/csv; charset=utf-8",
            fileDownloadName: job.FileName);
    }

    private ActionResult? ResolveCaller(
        out string? userId,
        out ExportCallerRole callerRole)
    {
        userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        callerRole = default;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        if (User.IsInRole("Admin"))
        {
            callerRole = ExportCallerRole.Admin;
            return null;
        }

        if (User.IsInRole("Agent"))
        {
            callerRole = ExportCallerRole.Agent;
            return null;
        }

        if (User.IsInRole("User"))
        {
            callerRole = ExportCallerRole.User;
            return null;
        }

        return Forbid();
    }

    private static Dictionary<string, string[]> ValidateRequest(
        CreateExportRequestDto request,
        out ExportResourceType resourceType,
        out ExportFormat format,
        out ExportScope scope)
    {
        var errors = new Dictionary<string, string[]>();

        resourceType = ExportResourceType.Tickets;
        if (request.ResourceType != ExportResourceType.Tickets.ToString())
        {
            errors[nameof(request.ResourceType)] = ["ResourceType must be Tickets."];
        }

        format = ExportFormat.Csv;
        if (request.Format != ExportFormat.Csv.ToString())
        {
            errors[nameof(request.Format)] = ["Format must be Csv."];
        }

        if (!Enum.TryParse(request.Scope, false, out scope) ||
            !Enum.IsDefined(scope) ||
            request.Scope != scope.ToString())
        {
            errors[nameof(request.Scope)] = ["Scope must be one of: Own, Assigned, All."];
        }

        return errors;
    }

    private static bool IsScopeAllowed(
        ExportCallerRole callerRole,
        ExportScope scope) => callerRole switch
        {
            ExportCallerRole.Admin => scope == ExportScope.All,
            ExportCallerRole.Agent => scope is ExportScope.Own or ExportScope.Assigned,
            ExportCallerRole.User => scope == ExportScope.Own,
            _ => false
        };

    private BadRequestObjectResult ValidationError(
        Dictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails(errors)
        {
            Title = "Invalid export request",
            Detail = "Correct the validation errors and try again.",
            Status = StatusCodes.Status400BadRequest,
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        details.Extensions["code"] = "validation_error";
        return BadRequest(details);
    }

    private ObjectResult ActiveExportLimitReached()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Active export limit reached",
            Detail = "Wait for an existing export to finish before creating another.",
            Type = "https://httpstatuses.com/429",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "active_export_limit_reached";
        return StatusCode(StatusCodes.Status429TooManyRequests, problem);
    }

    private enum ExportCallerRole
    {
        User,
        Agent,
        Admin
    }
}
