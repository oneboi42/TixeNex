using System.Security.Claims;

namespace HelpDeskHero.Api.Application.TicketVisibility;

public interface ITicketVisibilityContextResolver
{
    TicketVisibilityResolution Resolve(ClaimsPrincipal principal);
}
