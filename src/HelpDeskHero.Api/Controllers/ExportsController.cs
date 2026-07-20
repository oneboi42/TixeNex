using System.Security.Claims;
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

    public ExportsController(AppDbContext db, IMessagePublisher publisher)
    {
        _db = db;
        _publisher = publisher;
    }

    [HttpPost]
    public async Task<IActionResult> CreateExport()
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
        await _db.SaveChangesAsync();

        await _publisher.PublishAsync(new ExportRequested(job.Id, job.UserId));

        return Accepted($"/api/exports/{job.Id}", new { job.Id, job.Status });
    }


    [HttpGet]
    public async Task<IActionResult> GetMyExports()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null)
        {
            return Unauthorized();
        }

        var exports = await _db.ExportJobs
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new
            {
                id = x.Id,
                status = x.Status.ToString(),
                fileName = x.FileName,
                createdAt = x.CreatedAt,
                completedAt = x.CompletedAt
            })
            .ToListAsync();

        return Ok(exports);
    }
}