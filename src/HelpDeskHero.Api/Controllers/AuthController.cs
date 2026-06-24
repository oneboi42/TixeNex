using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Services;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    public AuthController(
        SignInManager<ApplicationUser> signInManager,
        UserManager<ApplicationUser> userManager,
        TokenService tokenService,
        RefreshTokenService refreshTokenService,
        AppDbContext db)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _tokenService = tokenService;
        _refreshTokenService = refreshTokenService;
        _db = db;
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

        var (accessToken, accessExp) = await _tokenService.CreateAccessTokenAsync(user);

        var (refreshToken, refreshExp) = await _refreshTokenService.CreateAsync(
            user.Id,
            dto.DeviceName,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct);

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new TokenResponseDto
        {
            AccessToken = accessToken,
            AccessTokenExpiresAtUtc = accessExp,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAtUtc = refreshExp,
            UserName = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Roles = roles.ToArray()
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<TokenResponseDto>> Refresh(RefreshRequestDto dto, CancellationToken ct)
    {
        var refresh = await _refreshTokenService.GetActiveByRawTokenAsync(dto.RefreshToken, ct);

        if (refresh is null || refresh.User is null || !refresh.IsActive || !refresh.User.IsActive)
            return Unauthorized();

        await _refreshTokenService.RevokeAsync(refresh, ct);

        var user = refresh.User;

        var (accessToken, accessExp) = await _tokenService.CreateAccessTokenAsync(user);

        var (newRefreshToken, refreshExp) = await _refreshTokenService.CreateAsync(
            user.Id,
            dto.DeviceName,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            ct);

        var roles = await _userManager.GetRolesAsync(user);

        return Ok(new TokenResponseDto
        {
            AccessToken = accessToken,
            AccessTokenExpiresAtUtc = accessExp,
            RefreshToken = newRefreshToken,
            RefreshTokenExpiresAtUtc = refreshExp,
            UserName = user.UserName ?? string.Empty,
            DisplayName = user.DisplayName,
            Roles = roles.ToArray()
        });
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
}