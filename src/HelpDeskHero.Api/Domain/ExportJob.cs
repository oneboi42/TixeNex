namespace HelpDeskHero.Api.Domain;

public class ExportJob
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public ExportStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FileName { get; set; }
}