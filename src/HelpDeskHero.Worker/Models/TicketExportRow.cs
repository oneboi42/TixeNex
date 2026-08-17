namespace HelpDeskHero.Worker.Models;

public sealed class TicketExportRow
{
    public int Id { get; set; }
    public string TicketNumber { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public string Requester { get; set; } = string.Empty;
    public string AssignedTo { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public DateTime? FirstRespondedAtUtc { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime? DueFirstResponseAtUtc { get; set; }
    public DateTime? DueResolveAtUtc { get; set; }
}