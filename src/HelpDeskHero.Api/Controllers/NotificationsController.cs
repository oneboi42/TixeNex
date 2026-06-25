using System.Security.Claims;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Shared.Contracts.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<UserNotificationDto>>> GetMine(CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return UnauthorizedProblem();

        var items = await _db.UserNotifications
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(20)
            .Select(x => new UserNotificationDto
            {
                Id = x.Id,
                Subject = x.Subject,
                Body = x.Body,
                IsRead = x.IsRead,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead(int id, CancellationToken ct)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return UnauthorizedProblem();

        var item = await _db.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct);

        if (item is null)
            return NotificationNotFound(id);

        item.IsRead = true;
        item.ReadAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    private ObjectResult UnauthorizedProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Brak identyfikatora uzytkownika",
            Detail = "Token nie zawiera identyfikatora zalogowanego uzytkownika.",
            Type = "https://httpstatuses.com/401",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "missing_user_id";

        return StatusCode(StatusCodes.Status401Unauthorized, problem);
    }

    private ObjectResult NotificationNotFound(int id)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Nie znaleziono powiadomienia",
            Detail = $"Powiadomienie o ID {id} nie istnieje albo nie nalezy do zalogowanego uzytkownika.",
            Type = "https://httpstatuses.com/404",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "notification_not_found";

        return StatusCode(StatusCodes.Status404NotFound, problem);
    }
}
