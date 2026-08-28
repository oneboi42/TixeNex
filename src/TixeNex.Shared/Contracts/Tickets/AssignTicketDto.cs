using System.ComponentModel.DataAnnotations;

namespace TixeNex.Shared.Contracts.Tickets;

public sealed class AssignTicketDto
{
    [Required]
    public string AssignedToUserId { get; set; } = string.Empty;

    [Required]
    public string RowVersionBase64 { get; set; } = string.Empty;
}