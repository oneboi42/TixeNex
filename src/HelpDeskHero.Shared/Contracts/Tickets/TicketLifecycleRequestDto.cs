using System.ComponentModel.DataAnnotations;

namespace HelpDeskHero.Shared.Contracts.Tickets;

public sealed class TicketLifecycleRequestDto
{
    [Required]
    public string RowVersionBase64 { get; set; } = string.Empty;
}
