namespace HelpDeskHero.Api.Domain;

public sealed class Ticket
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = "New";
    public string Priority { get; set; } = "Medium";
    public DateTime CreatedAtUtc { get; set; }

    // 4.1 update
    public DateTime? UpdatedAtUtc { get; set; }
}