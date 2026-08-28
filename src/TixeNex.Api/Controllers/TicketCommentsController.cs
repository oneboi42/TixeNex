using System.Security.Claims;
using TixeNex.Api.Application.TicketVisibility;
using TixeNex.Api.Domain;
using TixeNex.Api.BackgroundJobs.Contracts;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TixeNex.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:int}/comments")]
[Authorize]
public sealed class TicketCommentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly INotificationJob _notificationJob;
    private readonly ITicketVisibilityContextResolver _ticketVisibilityContextResolver;

    public TicketCommentsController(
        AppDbContext db,
        INotificationJob notificationJob,
        ITicketVisibilityContextResolver ticketVisibilityContextResolver)
    {
        _db = db;
        _notificationJob = notificationJob;
        _ticketVisibilityContextResolver = ticketVisibilityContextResolver;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketCommentDto>>> GetAll(int ticketId, CancellationToken ct)
    {
        var accessError = ResolveTicketVisibility(out var visibilityContext);
        if (accessError is not null)
            return accessError;

        if (!await _db.Tickets
                .ApplyVisibility(visibilityContext)
                .AnyAsync(x => x.Id == ticketId, ct))
            return TicketNotFound(ticketId);

        var items = await _db.TicketComments
            .AsNoTracking()
            .Where(x => x.TicketId == ticketId)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new TicketCommentDto
            {
                Id = x.Id,
                TicketId = x.TicketId,
                Body = x.Body,
                CreatedAtUtc = x.CreatedAtUtc,
                CreatedByDisplayName = x.CreatedByDisplayName
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<TicketCommentDto>> Create(
        int ticketId,
        CreateTicketCommentDto dto,
        CancellationToken ct)
    {
        var body = dto.Body.Trim();
        var errors = ValidateCreate(body);

        if (errors.Count > 0)
            return ValidationError(errors);

        var accessError = ResolveTicketVisibility(out var visibilityContext);
        if (accessError is not null)
            return accessError;

        if (!await _db.Tickets
                .ApplyVisibility(visibilityContext)
                .AnyAsync(x => x.Id == ticketId, ct))
            return TicketNotFound(ticketId);

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return UnauthorizedProblem();

        var entity = new TicketComment
        {
            TicketId = ticketId,
            Body = body,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = userId,
            CreatedByDisplayName = GetDisplayName()
        };

        _db.TicketComments.Add(entity);
        await _db.SaveChangesAsync(ct);

        await _notificationJob.SendTicketCommentNotificationsAsync(ticketId, userId, ct);

        var result = ToDto(entity);

        return CreatedAtAction(nameof(GetAll), new { ticketId }, result);
    }

    private ActionResult? ResolveTicketVisibility(out TicketVisibilityContext context)
    {
        var resolution = _ticketVisibilityContextResolver.Resolve(User);

        if (resolution.Status == TicketVisibilityResolutionStatus.Unauthorized)
        {
            context = null!;
            return Unauthorized();
        }

        if (resolution.Status == TicketVisibilityResolutionStatus.Forbidden)
        {
            context = null!;
            return Forbid();
        }

        context = resolution.Context!;
        return null;
    }

    private static Dictionary<string, string[]> ValidateCreate(string body)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(body))
        {
            errors["Body"] = ["Comment body is required."];
        }
        else if (body.Length > 4000)
        {
            errors["Body"] = ["Comment body cannot exceed 4000 characters."];
        }

        return errors;
    }

    private BadRequestObjectResult ValidationError(Dictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails(errors)
        {
            Title = "Invalid comment",
            Detail = "Correct the validation errors and try again.",
            Status = StatusCodes.Status400BadRequest,
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        details.Extensions["code"] = "validation_error";

        return BadRequest(details);
    }

    private ObjectResult TicketNotFound(int ticketId)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Ticket not found",
            Detail = $"The ticket with ID {ticketId} does not exist or has been deleted.",
            Type = "https://httpstatuses.com/404",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "ticket_not_found";

        return StatusCode(StatusCodes.Status404NotFound, problem);
    }

    private ObjectResult UnauthorizedProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Missing user ID",
            Detail = "The token does not contain the signed-in user's ID.",
            Type = "https://httpstatuses.com/401",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "missing_user_id";

        return StatusCode(StatusCodes.Status401Unauthorized, problem);
    }

    private string GetDisplayName()
    {
        return User.FindFirstValue("display_name")
            ?? User.Identity?.Name
            ?? "unknown";
    }

    private static TicketCommentDto ToDto(TicketComment entity) => new()
    {
        Id = entity.Id,
        TicketId = entity.TicketId,
        Body = entity.Body,
        CreatedAtUtc = entity.CreatedAtUtc,
        CreatedByDisplayName = entity.CreatedByDisplayName
    };
}
