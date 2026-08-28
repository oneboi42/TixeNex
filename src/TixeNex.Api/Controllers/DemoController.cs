using System.Data;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Api.Infrastructure.Security;
using TixeNex.Api.Infrastructure.Services;
using TixeNex.Shared.Contracts.Auth;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.RateLimiting;

namespace TixeNex.Api.Controllers;

[ApiController]
[Route("api/demo")]
public sealed class DemoController : ControllerBase
{
    private const string CapacityLockResource =
        "TixeNex:DemoSessionCapacity";

    private static readonly SemaphoreSlim NonRelationalCapacityGate =
        new(1, 1);

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
    [EnableRateLimiting("demo-session")]
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

        IDbContextTransaction? transaction = null;
        var nonRelationalGateHeld = false;

        try
        {
            if (_db.Database.IsSqlServer())
            {
                transaction = await _db.Database.BeginTransactionAsync(ct);
                await AcquireSqlServerCapacityLockAsync(ct);
            }
            else
            {
                await NonRelationalCapacityGate.WaitAsync(ct);
                nonRelationalGateHeld = true;
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
                Email = $"{userName}@demo.tixenex.local",
                DisplayName = $"Demo {role} {suffix}",
                EmailConfirmed = true,
                IsActive = true,

                IsDemoWorkspace = true,
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

            if (transaction is not null)
                await transaction.CommitAsync(ct);

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
        finally
        {
            if (transaction is not null)
                await transaction.DisposeAsync();

            if (nonRelationalGateHeld)
                NonRelationalCapacityGate.Release();
        }
    }

    private async Task AcquireSqlServerCapacityLockAsync(CancellationToken ct)
    {
        var lockResult = new SqlParameter("@result", SqlDbType.Int)
        {
            Direction = ParameterDirection.Output
        };

        await _db.Database.ExecuteSqlRawAsync(
            $"""
            EXEC @result = sys.sp_getapplock
                @Resource = N'{CapacityLockResource}',
                @LockMode = 'Exclusive',
                @LockOwner = 'Transaction',
                @LockTimeout = 30000;
            """,
            [lockResult],
            ct);

        if ((int)lockResult.Value < 0)
        {
            throw new InvalidOperationException(
                "Could not acquire the demo-session capacity lock.");
        }
    }
}
