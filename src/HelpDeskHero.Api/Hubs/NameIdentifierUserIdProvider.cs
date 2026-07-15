using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;

namespace HelpDeskHero.Api.Hubs;

/// <summary>Uses the same stable user id as the API's authorization and notification records.</summary>
public sealed class NameIdentifierUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? connection.User?.FindFirstValue("sub");
}
