using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Security;
using HelpDeskHero.Shared.Contracts.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace HelpDeskHero.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOnly")]
public sealed class UsersController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UsersController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpGet("agents")]
    public async Task<ActionResult<IReadOnlyList<AssignableAgentDto>>> GetAgents(
        CancellationToken ct)
    {
        var agents = await _userManager.GetUsersInRoleAsync("Agent");
        var isDemoWorkspace = DemoWorkspaceClaims.IsDemoWorkspace(User);

        ct.ThrowIfCancellationRequested();

        var result = agents
            .Where(user =>
                user.IsActive &&
                user.IsDemoWorkspace == isDemoWorkspace)
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.UserName)
            .Select(user => new AssignableAgentDto
            {
                Id = user.Id,
                DisplayName = user.DisplayName,
                UserName = user.UserName ?? string.Empty
            })
            .ToList();

        return Ok(result);
    }
}
