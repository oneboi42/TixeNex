using System.Security.Claims;
using HelpDeskHero.Api.Application.TicketVisibility;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Storage;
using HelpDeskHero.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/tickets/{ticketId:int}/attachments")]
[Authorize]
public sealed class TicketAttachmentsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ITicketVisibilityContextResolver _ticketVisibilityContextResolver;

    public TicketAttachmentsController(
        AppDbContext db,
        IFileStorage storage,
        ITicketVisibilityContextResolver ticketVisibilityContextResolver)
    {
        _db = db;
        _storage = storage;
        _ticketVisibilityContextResolver = ticketVisibilityContextResolver;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<TicketAttachmentDto>>> GetAll(int ticketId, CancellationToken ct)
    {
        var accessError = ResolveTicketVisibility(out var visibilityContext);
        if (accessError is not null)
            return accessError;

        if (!await _db.Tickets
                .ApplyVisibility(visibilityContext)
                .AnyAsync(x => x.Id == ticketId, ct))
            return TicketNotFound(ticketId);

        var items = await _db.TicketAttachments
            .AsNoTracking()
            .Where(x => x.TicketId == ticketId)
            .OrderByDescending(x => x.UploadedAtUtc)
            .Select(x => new TicketAttachmentDto
            {
                Id = x.Id,
                TicketId = x.TicketId,
                OriginalFileName = x.OriginalFileName,
                ContentType = x.ContentType,
                SizeBytes = x.SizeBytes,
                UploadedAtUtc = x.UploadedAtUtc,
                UploadedByUserId = x.UploadedByUserId
            })
            .ToListAsync(ct);

        return Ok(items);
    }

    [HttpPost]
    [RequestSizeLimit(AttachmentValidation.MaxSizeBytes)]
    public async Task<ActionResult<TicketAttachmentDto>> Upload(
        int ticketId,
        IFormFile? file,
        CancellationToken ct)
    {
        var errors = AttachmentValidation.Validate(file);
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

        var stored = await _storage.SaveAsync(file!, ct);

        var entity = new TicketAttachment
        {
            TicketId = ticketId,
            OriginalFileName = stored.OriginalFileName,
            StoredFileName = stored.StoredFileName,
            RelativePath = stored.RelativePath,
            ContentType = stored.ContentType,
            SizeBytes = stored.SizeBytes,
            UploadedAtUtc = DateTime.UtcNow,
            UploadedByUserId = userId
        };

        _db.TicketAttachments.Add(entity);
        await _db.SaveChangesAsync(ct);

        return Ok(ToDto(entity));
    }

    [HttpGet("{attachmentId:int}/download")]
    public async Task<IActionResult> Download(int ticketId, int attachmentId, CancellationToken ct)
    {
        var accessError = ResolveTicketVisibility(out var visibilityContext);
        if (accessError is not null)
            return accessError;

        if (!await _db.Tickets
                .ApplyVisibility(visibilityContext)
                .AnyAsync(x => x.Id == ticketId, ct))
            return TicketNotFound(ticketId);

        var item = await _db.TicketAttachments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.TicketId == ticketId, ct);

        if (item is null)
            return AttachmentNotFound(attachmentId);

        var stream = await _storage.OpenReadAsync(item.RelativePath, ct);
        return File(stream, item.ContentType, item.OriginalFileName);
    }

    [HttpDelete("{attachmentId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int ticketId, int attachmentId, CancellationToken ct)
    {
        var accessError = ResolveTicketVisibility(out var visibilityContext);
        if (accessError is not null)
            return accessError;

        if (!await _db.Tickets
                .ApplyVisibility(visibilityContext)
                .AnyAsync(x => x.Id == ticketId, ct))
            return TicketNotFound(ticketId);

        var item = await _db.TicketAttachments
            .FirstOrDefaultAsync(x => x.Id == attachmentId && x.TicketId == ticketId, ct);

        if (item is null)
            return AttachmentNotFound(attachmentId);

        await _storage.DeleteAsync(item.RelativePath, ct);

        _db.TicketAttachments.Remove(item);
        await _db.SaveChangesAsync(ct);

        return NoContent();
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

    private BadRequestObjectResult ValidationError(Dictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails(errors)
        {
            Title = "Invalid file",
            Detail = "Correct the file validation errors and try again.",
            Status = StatusCodes.Status400BadRequest,
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        details.Extensions["code"] = "invalid_attachment";

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

    private ObjectResult AttachmentNotFound(int attachmentId)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Attachment not found",
            Detail = $"The attachment with ID {attachmentId} does not exist or does not belong to this ticket.",
            Type = "https://httpstatuses.com/404",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "attachment_not_found";

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

    private static TicketAttachmentDto ToDto(TicketAttachment entity) => new()
    {
        Id = entity.Id,
        TicketId = entity.TicketId,
        OriginalFileName = entity.OriginalFileName,
        ContentType = entity.ContentType,
        SizeBytes = entity.SizeBytes,
        UploadedAtUtc = entity.UploadedAtUtc,
        UploadedByUserId = entity.UploadedByUserId
    };
}
