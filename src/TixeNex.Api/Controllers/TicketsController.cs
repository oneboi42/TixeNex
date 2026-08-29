using Hangfire;
using System.Security.Claims;
using TixeNex.Api.Application.Interfaces;
using TixeNex.Api.Application.TicketVisibility;
using TixeNex.Api.BackgroundJobs.Contracts;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Api.Infrastructure.Services;
using TixeNex.Shared.Contracts.Common;
using TixeNex.Shared.Contracts.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace TixeNex.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public sealed class TicketsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly AuditService _audit;
    private readonly ISlaCalculator _slaCalculator;
    private readonly ITicketAssignmentService _ticketAssignmentService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITicketVisibilityContextResolver _ticketVisibilityContextResolver;
    private readonly IOutboxWriter _outboxWriter;
    private readonly IWebHostEnvironment _environment;

    public TicketsController(
        AppDbContext db,
        AuditService audit,
        ISlaCalculator slaCalculator,
        ITicketAssignmentService ticketAssignmentService,
        UserManager<ApplicationUser> userManager,
        ITicketVisibilityContextResolver ticketVisibilityContextResolver,
        IOutboxWriter outboxWriter,
        IWebHostEnvironment environment)
    {
        _db = db;
        _audit = audit;
        _slaCalculator = slaCalculator;
        _ticketAssignmentService = ticketAssignmentService;
        _userManager = userManager;
        _ticketVisibilityContextResolver = ticketVisibilityContextResolver;
        _outboxWriter = outboxWriter;
        _environment = environment;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResultDto<TicketDto>>> GetAll([FromQuery] TicketQueryDto query, CancellationToken ct)
    {
        var accessError = ResolveTicketVisibility(out var visibilityContext);

        if (accessError is not null)
            return accessError;

        var pageNumber = query.PageNumber < 1 ? 1 : query.PageNumber;
        var pageSize = query.PageSize < 1 ? 10 : query.PageSize;
        pageSize = pageSize > 100 ? 100 : pageSize;

        var q = _db.Tickets.ApplyVisibility(visibilityContext);

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

        var pagedTickets = q
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize);
        var includeRequesterDisplayName = User.IsInRole("Admin");

        var items = await (
            from x in pagedTickets
            join assignedUser in _db.Users
                on x.AssignedToUserId equals assignedUser.Id into assignedUsers
            from assignedUser in assignedUsers.DefaultIfEmpty()
            join requesterUser in _db.Users
                on x.RequesterUserId equals requesterUser.Id into requesterUsers
            from requesterUser in requesterUsers.DefaultIfEmpty()
            select new TicketDto
            {
                Id = x.Id,
                Number = x.Number,
                Title = x.Title,
                Description = x.Description,
                Status = x.Status,
                Priority = x.Priority,
                CreatedAtUtc = x.CreatedAtUtc,
                UpdatedAtUtc = x.UpdatedAtUtc,
                RequesterDisplayName =
                    requesterUser != null &&
                    (includeRequesterDisplayName || requesterUser.IsDemoUser)
                        ? requesterUser.DisplayName
                        : null,
                IsRequesterCurrentUser =
                    x.RequesterUserId == visibilityContext.UserId,
                IsDemoTicket =
                    x.Origin != TicketOrigin.Normal,
                DemoExpiresAtUtc =
                    x.DemoExpiresAtUtc,
                AssignedToUserId = x.AssignedToUserId,
                AssignedToDisplayName = assignedUser == null
                    ? null
                    : assignedUser.DisplayName,
                CanEdit = visibilityContext.Scope == TicketVisibilityScope.All ||
                    visibilityContext.Scope == TicketVisibilityScope.Assigned &&
                    x.AssignedToUserId == visibilityContext.UserId,
                CanStart = x.Status == "New" &&
                    (visibilityContext.Scope == TicketVisibilityScope.All ||
                     visibilityContext.Scope == TicketVisibilityScope.Assigned &&
                     x.AssignedToUserId == visibilityContext.UserId),
                CanResolve = x.Status == "InProgress" &&
                    (visibilityContext.Scope == TicketVisibilityScope.All ||
                     visibilityContext.Scope == TicketVisibilityScope.Assigned &&
                     x.AssignedToUserId == visibilityContext.UserId),
                CanClose = x.Status == "Resolved" &&
                    (visibilityContext.Scope == TicketVisibilityScope.All ||
                     x.RequesterUserId == visibilityContext.UserId),
                CanReopen = x.Status == "Resolved" &&
                    (visibilityContext.Scope == TicketVisibilityScope.All ||
                     x.RequesterUserId == visibilityContext.UserId),
                RowVersionBase64 = Convert.ToBase64String(x.RowVersion)
            }).ToListAsync(ct);

        return Ok(new PagedResultDto<TicketDto>
        {
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = items
        });
    }

    [HttpGet("deleted")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<List<TicketDto>>> GetDeleted(CancellationToken ct)
    {
        var accessError = ResolveTicketVisibility(out var visibilityContext);

        if (accessError is not null)
            return accessError;

        var items = await _db.Tickets
            .IgnoreQueryFilters()
            .ApplyWorkspace(visibilityContext)
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
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Restore(int id, CancellationToken ct)
    {
        var accessError = ResolveTicketVisibility(out var visibilityContext);

        if (accessError is not null)
            return accessError;

        var ticket = await _db.Tickets
            .IgnoreQueryFilters()
            .ApplyWorkspace(visibilityContext)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (ticket is null)
            return TicketNotFound(id);

        if (!ticket.IsDeleted)
        {
            return BusinessProblem(
                StatusCodes.Status400BadRequest,
                "Ticket is not in the recycle bin",
                "A ticket that has not been deleted cannot be restored.");
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
        var accessError = ResolveTicketVisibility(out var visibilityContext);

        if (accessError is not null)
            return accessError;

        var result = await (
            from ticket in _db.Tickets.ApplyVisibility(visibilityContext)
            join assignedUser in _db.Users
                on ticket.AssignedToUserId equals assignedUser.Id into assignedUsers
            from assignedUser in assignedUsers.DefaultIfEmpty()
            join requesterUser in _db.Users
                on ticket.RequesterUserId equals requesterUser.Id into requesterUsers
            from requesterUser in requesterUsers.DefaultIfEmpty()
            where ticket.Id == id
            select new
            {
                Ticket = ticket,
                AssignedToDisplayName = assignedUser == null
                    ? null
                    : assignedUser.DisplayName,
                RequesterDisplayName = requesterUser == null
                    ? null
                    : requesterUser.DisplayName,
                RequesterIsDemoUser =
                    requesterUser != null &&
                    requesterUser.IsDemoUser
            }).FirstOrDefaultAsync(ct);

        if (result is null)
            return TicketNotFound(id);

        return Ok(ToDto(
            result.Ticket,
            visibilityContext,
            result.AssignedToDisplayName,
            User.IsInRole("Admin") || result.RequesterIsDemoUser
                ? result.RequesterDisplayName
                : null));
    }

    [HttpPost]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<ActionResult<TicketDto>> Create(CreateTicketDto dto, CancellationToken ct)
    {
        var errors = ValidateCreate(dto);

        if (errors.Count > 0)
            return ValidationError(errors);

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(currentUserId))
            return Unauthorized();

        var currentUser = await _userManager.FindByIdAsync(currentUserId);

        if (currentUser is null || !currentUser.IsActive)
            return Unauthorized();

        var now = DateTime.UtcNow;

        if (currentUser.IsDemoUser)
        {
            if (!currentUser.DemoExpiresAtUtc.HasValue ||
                !currentUser.DemoAbsoluteExpiresAtUtc.HasValue ||
                currentUser.DemoExpiresAtUtc <= now ||
                currentUser.DemoAbsoluteExpiresAtUtc <= now)
            {
                return Unauthorized();
            }
        }

        var numberSuffix = Guid.NewGuid()
            .ToString("N")[..6]
            .ToUpperInvariant();

        var nextNumber = $"HDH-{now:yyyyMMddHHmmss}-{numberSuffix}";

        var entity = new Ticket
        {
            Number = nextNumber,
            Title = dto.Title.Trim(),
            Description = dto.Description.Trim(),
            Priority = dto.Priority,
            Status = "New",
            Origin = currentUser.IsDemoUser ? TicketOrigin.DemoUser : TicketOrigin.Normal,
            CreatedAtUtc = now,
            RequesterUserId = currentUserId,
            DemoExpiresAtUtc = currentUser.IsDemoWorkspace
                ? currentUser.DemoAbsoluteExpiresAtUtc
                : null
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

            if (!string.IsNullOrWhiteSpace(entity.AssignedToUserId) &&
                entity.AssignedToUserId != entity.RequesterUserId)
            {
                BackgroundJob.Enqueue<INotificationJob>(job =>
                    job.SendTicketAssignedNotificationAsync(
                        entity.Id,
                        entity.AssignedToUserId,
                        false,
                        default));
            }
        }

        var visibilityContext = _ticketVisibilityContextResolver.Resolve(User).Context!;
        var assignedToDisplayName = entity.AssignedToUserId is null
            ? null
            : await _db.Users
                .Where(x => x.Id == entity.AssignedToUserId)
                .Select(x => x.DisplayName)
                .SingleOrDefaultAsync(ct);
        var requesterDisplayName =
            User.IsInRole("Admin") || currentUser.IsDemoUser
                ? currentUser.DisplayName
                : null;
        var result = ToDto(
            entity,
            visibilityContext,
            assignedToDisplayName,
            requesterDisplayName);

        return CreatedAtAction(nameof(GetById), new { id = entity.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = "CanManageTickets")]
    public async Task<IActionResult> Update(int id, UpdateTicketDto dto, CancellationToken ct)
    {
        var errors = ValidateUpdate(dto);

        if (errors.Count > 0)
            return ValidationError(errors);

        var accessError = ResolveTicketVisibility(out var visibilityContext);

        if (accessError is not null)
            return accessError;

        var entity = await _db.Tickets
            .ApplyWorkspace(visibilityContext)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
            return TicketNotFound(id);

        if (!TicketPermissions.CanEdit(entity, visibilityContext))
            return Forbid();

        if (dto.Status != entity.Status)
        {
            return BusinessProblem(
                StatusCodes.Status409Conflict,
                "Zmiana statusu wymaga dedykowanej akcji",
                "Use the dedicated ticket lifecycle endpoint.",
                "status_change_requires_lifecycle_endpoint");
        }

        var originalPriority = entity.Priority;

        var originalRowVersion = Convert.FromBase64String(dto.RowVersionBase64);
        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = originalRowVersion;

        entity.Title = dto.Title.Trim();
        entity.Description = dto.Description.Trim();
        entity.Priority = dto.Priority;
        entity.UpdatedAtUtc = DateTime.UtcNow;

        if (originalPriority != entity.Priority)
        {
            await _slaCalculator.ApplySlaAsync(entity, ct);
        }

        await _outboxWriter.AddAsync(
            "TicketChanged",
            ToLiveUpdateDto(entity, "Updated"),
            ct);

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


    [HttpPost("{id:int}/assign")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Assign(int id, AssignTicketDto dto, CancellationToken ct)
    {
        var errors = ValidateAssignment(dto);

        if (errors.Count > 0)
            return ValidationError(errors);

        var accessError = ResolveTicketVisibility(out var visibilityContext);

        if (accessError is not null)
            return accessError;

        var entity = await _db.Tickets
            .ApplyWorkspace(visibilityContext)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
            return TicketNotFound(id);

        if (entity.Status == "Closed")
        {
            return BusinessProblem(
                StatusCodes.Status409Conflict,
                "Closed ticket cannot be reassigned",
                "A closed ticket cannot be assigned or reassigned.",
                "closed_ticket_cannot_be_assigned");
        }

        var assignedToUserId = dto.AssignedToUserId.Trim();
        var assignee = await _userManager.FindByIdAsync(assignedToUserId);

        if (assignee is null)
        {
            return BusinessProblem(
                StatusCodes.Status404NotFound,
                "Assignee not found",
                $"The user with ID {assignedToUserId} does not exist.",
                "assignee_not_found");
        }

        if (!assignee.IsActive)
        {
            return BusinessProblem(
                StatusCodes.Status409Conflict,
                "Inactive assignee",
                "An inactive user cannot be assigned to a ticket.",
                "assignee_inactive");
        }

        if (!await _userManager.IsInRoleAsync(assignee, "Agent"))
        {
            return BusinessProblem(
                StatusCodes.Status409Conflict,
                "Invalid assignee",
                "Only a user with the Agent role can be assigned to a ticket.",
                "assignee_must_be_agent");
        }

        var ticketIsDemoWorkspace = entity.DemoExpiresAtUtc is not null;

        if (assignee.IsDemoWorkspace != ticketIsDemoWorkspace)
        {
            return BusinessProblem(
                StatusCodes.Status409Conflict,
                "Invalid assignee workspace",
                "Ticket and assignee must belong to the same workspace.",
                "assignee_workspace_mismatch");
        }

        var originalRowVersion = Convert.FromBase64String(dto.RowVersionBase64);
        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = originalRowVersion;

        var previousAssignedToUserId = entity.AssignedToUserId;

        if (previousAssignedToUserId == assignee.Id)
            return NoContent();

        var action = previousAssignedToUserId is null ? "Assign" : "Reassign";
        var eventType = previousAssignedToUserId is null ? "Assigned" : "Reassigned";
        var previousStatus = entity.Status;

        entity.AssignedToUserId = assignee.Id;
        entity.Status = "New";
        entity.UpdatedAtUtc = DateTime.UtcNow;
        entity.ResolvedAtUtc = null;

        await _outboxWriter.AddAsync(
            "TicketChanged",
            ToLiveUpdateDto(entity, eventType),
            ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConflictProblem();
        }

        await _audit.WriteAsync(
            action,
            "Ticket",
            entity.Id.ToString(),
            new
            {
                entity.Number,
                entity.Title,
                PreviousAssignedToUserId = previousAssignedToUserId,
                AssignedToUserId = assignee.Id,
                AssignedToDisplayName = assignee.DisplayName,
                PreviousStatus = previousStatus,
                NewStatus = entity.Status
            },
            ct);

        if (!_environment.IsEnvironment("Testing"))
        {
            var isReassignment = previousAssignedToUserId != null;
            BackgroundJob.Enqueue<INotificationJob>(job =>
                job.SendTicketAssignedNotificationAsync(
                    entity.Id,
                    assignee.Id,
                    isReassignment,
                    default));
        }

        return NoContent();
    }

    [HttpPost("{id:int}/start")]
    public Task<IActionResult> Start(int id, TicketLifecycleRequestDto dto, CancellationToken ct) =>
        TransitionAsync(id, dto, "New", "InProgress", "Started", TicketPermissions.CanWork, ct);

    [HttpPost("{id:int}/resolve")]
    public Task<IActionResult> Resolve(int id, TicketLifecycleRequestDto dto, CancellationToken ct) =>
        TransitionAsync(id, dto, "InProgress", "Resolved", "Resolved", TicketPermissions.CanWork, ct);

    [HttpPost("{id:int}/close")]
    public Task<IActionResult> Close(int id, TicketLifecycleRequestDto dto, CancellationToken ct) =>
        TransitionAsync(id, dto, "Resolved", "Closed", "Closed", TicketPermissions.CanManageAsRequester, ct);

    [HttpPost("{id:int}/reopen")]
    public Task<IActionResult> Reopen(int id, TicketLifecycleRequestDto dto, CancellationToken ct) =>
        TransitionAsync(id, dto, "Resolved", "InProgress", "Reopened", TicketPermissions.CanManageAsRequester, ct);

    [HttpDelete("{id:int}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> SoftDelete(int id, CancellationToken ct)
    {
        var accessError = ResolveTicketVisibility(out var visibilityContext);

        if (accessError is not null)
            return accessError;

        var entity = await _db.Tickets
            .ApplyWorkspace(visibilityContext)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

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

    private static readonly string[] AllowedStatuses =
    [
        "New",
        "InProgress",
        "Resolved",
        "Closed"
    ];

    private async Task<IActionResult> TransitionAsync(
        int id,
        TicketLifecycleRequestDto dto,
        string requiredStatus,
        string targetStatus,
        string action,
        Func<Ticket, TicketVisibilityContext, bool> permission,
        CancellationToken ct)
    {
        var errors = ValidateRowVersion(dto.RowVersionBase64);

        if (errors.Count > 0)
            return ValidationError(errors);

        var accessError = ResolveTicketVisibility(out var visibilityContext);

        if (accessError is not null)
            return accessError;

        var entity = await _db.Tickets
            .ApplyWorkspace(visibilityContext)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (entity is null)
            return TicketNotFound(id);

        if (!permission(entity, visibilityContext))
            return Forbid();

        if (entity.Status != requiredStatus)
        {
            return BusinessProblem(
                StatusCodes.Status409Conflict,
                "Nieprawidlowa zmiana statusu",
                $"Status {entity.Status} does not allow the {action} action.",
                "invalid_status_transition");
        }

        var originalRowVersion = Convert.FromBase64String(dto.RowVersionBase64);
        _db.Entry(entity).Property(x => x.RowVersion).OriginalValue = originalRowVersion;

        var now = DateTime.UtcNow;
        entity.Status = targetStatus;
        entity.UpdatedAtUtc = now;

        if (action == "Started" && entity.FirstRespondedAtUtc is null)
            entity.FirstRespondedAtUtc = now;

        if (action == "Resolved")
            entity.ResolvedAtUtc = now;

        if (action == "Reopened")
            entity.ResolvedAtUtc = null;

        await _outboxWriter.AddAsync(
            "TicketChanged",
            ToLiveUpdateDto(entity, action),
            ct);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ConflictProblem();
        }

        await _audit.WriteAsync(
            action,
            "Ticket",
            entity.Id.ToString(),
            new { entity.Number, entity.Title },
            ct);

        return NoContent();
    }

    private BadRequestObjectResult ValidationError(Dictionary<string, string[]> errors)
    {
        var details = new ValidationProblemDetails(errors)
        {
            Title = "Invalid ticket data",
            Detail = "Correct the validation errors and try again.",
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
            "Ticket not found",
            $"The ticket with ID {id} does not exist or has been deleted.",
            "ticket_not_found");
    }

    private ObjectResult ConflictProblem()
    {
        return BusinessProblem(
            StatusCodes.Status409Conflict,
            "Konflikt danych",
            "The ticket was changed by another user. Refresh the page and try again.",
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


    private static Dictionary<string, string[]> ValidateAssignment(AssignTicketDto dto)
    {
        var errors = ValidateRowVersion(dto.RowVersionBase64);

        if (string.IsNullOrWhiteSpace(dto.AssignedToUserId))
        {
            errors["AssignedToUserId"] = ["AssignedToUserId is required."];
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

        foreach (var error in ValidateRowVersion(dto.RowVersionBase64))
            errors[error.Key] = error.Value;

        return errors;
    }

    private static Dictionary<string, string[]> ValidateRowVersion(string rowVersionBase64)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(rowVersionBase64))
        {
            errors["RowVersionBase64"] = ["RowVersion is required."];
        }
        else
        {
            try
            {
                Convert.FromBase64String(rowVersionBase64);
            }
            catch
            {
                errors["RowVersionBase64"] = ["RowVersion has invalid format."];
            }
        }

        return errors;
    }

    private static TicketDto ToDto(
        Ticket entity,
        TicketVisibilityContext context,
        string? assignedToDisplayName = null,
        string? requesterDisplayName = null) => new()
    {
        Id = entity.Id,
        Number = entity.Number,
        Title = entity.Title,
        Description = entity.Description,
        Status = entity.Status,
        Priority = entity.Priority,
        CreatedAtUtc = entity.CreatedAtUtc,
        UpdatedAtUtc = entity.UpdatedAtUtc,
        RequesterDisplayName = requesterDisplayName,
        IsRequesterCurrentUser = entity.RequesterUserId == context.UserId,
        IsDemoTicket = entity.Origin != TicketOrigin.Normal,
        DemoExpiresAtUtc = entity.DemoExpiresAtUtc,
        AssignedToUserId = entity.AssignedToUserId,
        AssignedToDisplayName = entity.AssignedToUserId is null
            ? null
            : assignedToDisplayName,
        CanEdit = TicketPermissions.CanEdit(entity, context),
        CanStart = TicketPermissions.CanStart(entity, context),
        CanResolve = TicketPermissions.CanResolve(entity, context),
        CanClose = TicketPermissions.CanClose(entity, context),
        CanReopen = TicketPermissions.CanReopen(entity, context),
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
