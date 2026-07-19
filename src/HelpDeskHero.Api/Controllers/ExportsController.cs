using System.Security.Claims;
using HelpDeskHero.Api.Application.Interfaces;
using HelpDeskHero.Api.Application.Services.Exports;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Требует авторизации
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

        // Важно: публикуем строго ПОСЛЕ успешного сохранения в БД
        await _publisher.PublishAsync(new ExportRequested(job.Id, job.UserId));

        // Возвращаем 202 Accepted
        return Accepted($"/api/exports/{job.Id}", new { job.Id, job.Status });
    }
}