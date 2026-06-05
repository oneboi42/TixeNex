using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Api.Infrastructure.Security;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ITokenService _tokenService;
    private readonly JwtOptions _jwt;

    public AuthController(
        AppDbContext db,
        ITokenService tokenService,
        IOptions<JwtOptions> jwtOptions)
    {
        _db = db;
        _tokenService = tokenService;
        _jwt = jwtOptions.Value;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginRequestDto dto, CancellationToken ct)
    {
        var user = await _db.Users
            .Include(x => x.RefreshTokens)
            .SingleOrDefaultAsync(x => x.UserName == dto.UserName, ct);

        if (user is null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return Unauthorized("Invalid username or password.");

        var accessExpires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        var accessToken = _tokenService.CreateAccessToken(user, accessExpires);
        var refreshToken = _tokenService.CreateRefreshToken(refreshExpires);

        user.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync(ct);

        return Ok(new AuthResponseDto
        {
            AccessToken = accessToken,
            AccessTokenExpiresAtUtc = accessExpires,
            RefreshToken = refreshToken.Token,
            RefreshTokenExpiresAtUtc = refreshExpires,
            UserName = user.UserName,
            Role = user.Role
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponseDto>> Refresh(RefreshTokenRequestDto dto, CancellationToken ct)
    {
        var principal = _tokenService.GetPrincipalFromExpiredToken(dto.AccessToken);
        if (principal is null)
            return Unauthorized("Invalid access token.");

        var userName = principal.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Unauthorized("Invalid identity.");

        var user = await _db.Users
            .Include(x => x.RefreshTokens)
            .SingleOrDefaultAsync(x => x.UserName == userName, ct);

        if (user is null)
            return Unauthorized("User not found.");

        var existingRefresh = user.RefreshTokens
            .OrderByDescending(x => x.Id)
            .FirstOrDefault(x => x.Token == dto.RefreshToken);

        if (existingRefresh is null || !existingRefresh.IsActive)
            return Unauthorized("Invalid refresh token.");

        existingRefresh.RevokedAtUtc = DateTime.UtcNow;

        var accessExpires = DateTime.UtcNow.AddMinutes(_jwt.AccessTokenMinutes);
        var refreshExpires = DateTime.UtcNow.AddDays(_jwt.RefreshTokenDays);

        var newAccessToken = _tokenService.CreateAccessToken(user, accessExpires);
        var newRefreshToken = _tokenService.CreateRefreshToken(refreshExpires);

        existingRefresh.ReplacedByToken = newRefreshToken.Token;
        user.RefreshTokens.Add(newRefreshToken);

        await _db.SaveChangesAsync(ct);

        return Ok(new AuthResponseDto
        {
            AccessToken = newAccessToken,
            AccessTokenExpiresAtUtc = accessExpires,
            RefreshToken = newRefreshToken.Token,
            RefreshTokenExpiresAtUtc = refreshExpires,
            UserName = user.UserName,
            Role = user.Role
        });
    }

    [HttpPost("revoke")]
    [Authorize]
    public async Task<IActionResult> Revoke([FromBody] string refreshToken, CancellationToken ct)
    {
        var userName = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(userName))
            return Unauthorized();

        var user = await _db.Users
            .Include(x => x.RefreshTokens)
            .SingleOrDefaultAsync(x => x.UserName == userName, ct);

        if (user is null)
            return Unauthorized();

        var token = user.RefreshTokens.FirstOrDefault(x => x.Token == refreshToken);
        if (token is null)
            return NotFound();

        token.RevokedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}