using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Security;
using HelpDeskHero.Api.Infrastructure.Services;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _db;
    private readonly TokenService _tokenService;
    private readonly RefreshTokenService _refreshTokenService;
    private readonly DemoOptions _options;

    public DemoController(
        UserManager<ApplicationUser> userManager,
        AppDbContext db,
        TokenService tokenService,
        RefreshTokenService refreshTokenService,
        IOptions<DemoOptions> options)
    {
        _userManager = userManager;
        _db = db;
        _tokenService = tokenService;
        _refreshTokenService = refreshTokenService;
        _options = options.Value;
    }

    [HttpPost("sessions")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponseDto>> CreateSession(
        CreateDemoSessionRequestDto dto,
        CancellationToken ct)
    {
        if (!_options.Enabled)
            return NotFound();

        var role = _options.AllowedRoles.FirstOrDefault(
            allowedRole => string.Equals(
                allowedRole,
                dto.Role.Trim(),
                StringComparison.OrdinalIgnoreCase));

        if (role is null)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Unsupported demo role",
                Detail =
                    $"Allowed roles: {string.Join(", ", _options.AllowedRoles)}."
            });
        }

        var now = DateTime.UtcNow;

        var activeDemoUsers = await _db.Users.CountAsync(
            user =>
                user.IsDemoUser &&
                user.IsActive &&
                user.DemoExpiresAtUtc != null &&
                user.DemoExpiresAtUtc > now,
            ct);

        if (activeDemoUsers >= _options.MaxActiveUsers)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Title = "Demo capacity reached",
                    Detail =
                        "No more demo sessions can be created at this time."
                });
        }

        var suffix = Guid.NewGuid()
            .ToString("N")[..8]
            .ToUpperInvariant();

        var normalizedRole = role.ToLowerInvariant();

        var userName =
            $"demo-{normalizedRole}-{suffix.ToLowerInvariant()}";

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@demo.helpdeskhero.local",
            DisplayName = $"Demo {role} {suffix}",
            EmailConfirmed = true,
            IsActive = true,

            IsDemoUser = true,
            CreatedAtUtc = now,
            LastActivityAtUtc = now,

            DemoExpiresAtUtc =
                now.AddMinutes(
                    _options.SlidingLifetimeMinutes),

            DemoAbsoluteExpiresAtUtc =
                now.AddMinutes(
                    _options.AbsoluteLifetimeMinutes)
        };

        var createResult =
            await _userManager.CreateAsync(user);

        if (!createResult.Succeeded)
        {
            return Problem(
                title: "Demo account creation failed",
                detail: string.Join(
                    "; ",
                    createResult.Errors.Select(
                        error => error.Description)));
        }

        var roleResult =
            await _userManager.AddToRoleAsync(user, role);

        if (!roleResult.Succeeded)
        {
            await _userManager.DeleteAsync(user);

            return Problem(
                title: "Demo role assignment failed",
                detail: string.Join(
                    "; ",
                    roleResult.Errors.Select(
                        error => error.Description)));
        }

        var tokenLimit = user.DemoExpiresAtUtc;

        var (accessToken, accessExpiresAtUtc) =
            await _tokenService.CreateAccessTokenAsync(
                user,
                tokenLimit);

        var deviceName = string.IsNullOrWhiteSpace(dto.DeviceName)
            ? "Demo browser"
            : dto.DeviceName.Trim();

        var (refreshToken, refreshExpiresAtUtc) =
            await _refreshTokenService.CreateAsync(
                user.Id,
                deviceName,
                HttpContext.Connection
                    .RemoteIpAddress?
                    .ToString(),
                tokenLimit,
                ct);

        var roles =
            await _userManager.GetRolesAsync(user);

        return Ok(new TokenResponseDto
        {
            AccessToken = accessToken,
            AccessTokenExpiresAtUtc =
                accessExpiresAtUtc,

            RefreshToken = refreshToken,
            RefreshTokenExpiresAtUtc =
                refreshExpiresAtUtc,

            UserName = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Roles = roles.ToArray(),

            IsDemoUser = true,
            DemoExpiresAtUtc =
                user.DemoExpiresAtUtc,
            DemoAbsoluteExpiresAtUtc =
                user.DemoAbsoluteExpiresAtUtc
        });
    }
}