using System.Security.Claims;
using HelpDeskHero.Shared.Contracts.Exports;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Application.Services.Exports;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ExportsController : ControllerBase
{
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
    CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (userId is null)
        {
            return Unauthorized();
        }

        var job = new ExportJob
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Status = ExportStatus.Pending,
            CreatedAt = DateTime.UtcNow
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
                FileName = exportJob.FileName,
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
}