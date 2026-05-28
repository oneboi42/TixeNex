using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HelpDeskHero.Shared.Contracts.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;

using Microsoft.AspNetCore.Authorization;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public sealed class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public ActionResult<LoginResponseDto> Login(LoginRequestDto dto)
    {
        if (!IsValidUser(dto))
            return Unauthorized();

        var role = dto.UserName.Equals("admin", StringComparison.OrdinalIgnoreCase)
            ? "Admin"
            : "User";

        var token = CreateToken(dto.UserName, role);

        return Ok(new LoginResponseDto
        {
            Token = token.Token,
            ExpiresAtUtc = token.ExpiresAtUtc,
            UserName = dto.UserName,
            Role = role
        });
    }

    private static bool IsValidUser(LoginRequestDto dto)
    {
        return (dto.UserName == "admin" && dto.Password == "Admin123!")
            || (dto.UserName == "user" && dto.Password == "User123!");
    }

    private (string Token, DateTime ExpiresAtUtc) CreateToken(string userName, string role)
    {
        var jwtSection = _configuration.GetSection("Jwt");
        var key = jwtSection["Key"]
            ?? throw new InvalidOperationException("Missing Jwt:Key");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var expires = DateTime.UtcNow.AddHours(8);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userName),
            new(ClaimTypes.Role, role)
        };

        var jwt = new JwtSecurityToken(
            issuer: jwtSection["Issuer"],
            audience: jwtSection["Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expires,
            signingCredentials: credentials);

        var token = new JwtSecurityTokenHandler().WriteToken(jwt);
        return (token, expires);
    }
}