namespace HelpDeskHero.Shared.Contracts.Exports;

public sealed class ExportJobDto
{
    public Guid Id { get; set; }

    public string Status { get; set; } = string.Empty;

    public string? FileName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}