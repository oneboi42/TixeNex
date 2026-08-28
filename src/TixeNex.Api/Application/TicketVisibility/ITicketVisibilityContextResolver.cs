using System.Security.Claims;

namespace TixeNex.Api.Application.TicketVisibility;

public interface ITicketVisibilityContextResolver
{
    TicketVisibilityResolution Resolve(ClaimsPrincipal principal);
}
