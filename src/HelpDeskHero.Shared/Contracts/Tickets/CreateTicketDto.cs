using System.ComponentModel.DataAnnotations;

namespace HelpDeskHero.Shared.Contracts.Tickets;

public sealed class CreateTicketDto
{
    [Required]
    [StringLength(200, MinimumLength = 3)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [StringLength(4000, MinimumLength = 5)]
    public string Description { get; set; } = string.Empty;

    [Required]
    [RegularExpression("Low|Medium|High|Critical")]
    public string Priority { get; set; } = "Medium";
}