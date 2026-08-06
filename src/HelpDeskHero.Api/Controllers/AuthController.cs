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
using System.Security.Claims;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TokenService _tokenService;
    private readonly RefreshTokenService _refreshTokenService;
    private readonly AppDbContext _db;
    private readonly DemoOptions _demoOptions;

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        TokenService tokenService,
        RefreshTokenService refreshTokenService,
        AppDbContext db,
        IOptions<DemoOptions> demoOptions)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _tokenService = tokenService;
        _refreshTokenService = refreshTokenService;
        _db = db;
        _demoOptions = demoOptions.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponseDto>> Login(LoginRequestDto dto, CancellationToken ct)
    {
        var user = await _userManager.FindByNameAsync(dto.UserName);
        if (user is null || !user.IsActive)
            return Unauthorized();

        var result = await _signInManager.CheckPasswordSignInAsync(
            user,
            dto.Password,
            lockoutOnFailure: false);

        if (!result.Succeeded)
            return Unauthorized();

        return Ok(
            await CreateTokenResponseAsync(
                user,
                dto.DeviceName,
                ct));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponseDto>> Refresh(
        RefreshRequestDto dto,
        CancellationToken ct)
    {
        var refresh =
            await _refreshTokenService
                .GetActiveByRawTokenAsync(
                    dto.RefreshToken,
                    ct);

        if (refresh is null ||
            refresh.User is null ||
            !refresh.IsActive ||
            !refresh.User.IsActive)
        {
            return Unauthorized();
        }

        var user = refresh.User;
        var now = DateTime.UtcNow;

        if (user.IsDemoUser)
        {
            if (!user.DemoExpiresAtUtc.HasValue ||
                !user.DemoAbsoluteExpiresAtUtc.HasValue ||
                user.DemoExpiresAtUtc <= now ||
                user.DemoAbsoluteExpiresAtUtc <= now)
            {
                refresh.RevokedAtUtc = now;
                user.IsActive = false;

                await _db.SaveChangesAsync(ct);

                return Unauthorized();
            }

            var requestedExpiration =
                now.AddMinutes(
                    _demoOptions.SlidingLifetimeMinutes);

            user.DemoExpiresAtUtc =
                requestedExpiration <
                user.DemoAbsoluteExpiresAtUtc.Value
                    ? requestedExpiration
                    : user.DemoAbsoluteExpiresAtUtc.Value;

            user.LastActivityAtUtc = now;
        }

        refresh.RevokedAtUtc = now;

        await _db.SaveChangesAsync(ct);

        return Ok(
            await CreateTokenResponseAsync(
                user,
                dto.DeviceName,
                ct));
    }

    [HttpPost("revoke-all")]
    [Authorize]
    public async Task<IActionResult> RevokeAllSessions(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();

        var now = DateTime.UtcNow;

        var tokens = await _db.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null)
            .ToListAsync(ct);

        foreach (var token in tokens)
        {
            token.RevokedAtUtc = now;
        }

        await _db.SaveChangesAsync(ct);

        return NoContent();
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout(RefreshRequestDto dto, CancellationToken ct)
    {
        var refresh = await _refreshTokenService.GetActiveByRawTokenAsync(dto.RefreshToken, ct);

        if (refresh is not null)
        {
            await _refreshTokenService.RevokeAsync(refresh, ct);
        }

        return NoContent();
    }

    private async Task<TokenResponseDto>
        CreateTokenResponseAsync(
            ApplicationUser user,
            string deviceName,
            CancellationToken ct)
    {
        DateTime? tokenLimit = null;

        if (user.IsDemoUser)
        {
            tokenLimit = user.DemoExpiresAtUtc
                ?? throw new InvalidOperationException(
                    "Demo user does not have an expiration time.");
        }

        var (accessToken, accessExpiresAtUtc) =
            await _tokenService.CreateAccessTokenAsync(
                user,
                tokenLimit);

        var normalizedDeviceName =
            string.IsNullOrWhiteSpace(deviceName)
                ? "Unknown device"
                : deviceName.Trim();

        var (refreshToken, refreshExpiresAtUtc) =
            await _refreshTokenService.CreateAsync(
                user.Id,
                normalizedDeviceName,
                HttpContext.Connection
                    .RemoteIpAddress?
                    .ToString(),
                tokenLimit,
                ct);

        var roles =
            await _userManager.GetRolesAsync(user);

        return new TokenResponseDto
        {
            AccessToken = accessToken,
            AccessTokenExpiresAtUtc =
                accessExpiresAtUtc,

            RefreshToken = refreshToken,
            RefreshTokenExpiresAtUtc =
                refreshExpiresAtUtc,

            UserName =
                user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Roles = roles.ToArray(),

            IsDemoUser = user.IsDemoUser,
            DemoExpiresAtUtc =
                user.DemoExpiresAtUtc,
            DemoAbsoluteExpiresAtUtc =
                user.DemoAbsoluteExpiresAtUtc
        };
    }
}