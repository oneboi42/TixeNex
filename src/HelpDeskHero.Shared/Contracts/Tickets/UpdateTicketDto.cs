namespace HelpDeskHero.Shared.Contracts.Tickets;

public sealed class UpdateTicketDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "New";
    public string Priority { get; set; } = "Medium";
}