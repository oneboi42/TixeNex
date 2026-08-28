namespace TixeNex.Api.Domain;

public class ExportJob
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = default!;
    public ExportStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? FileName { get; set; }
    public string? StorageObjectName { get; set; }
    public ExportResourceType ResourceType { get; set; } = ExportResourceType.Tickets;
    public ExportFormat Format { get; set; } = ExportFormat.Csv;
    public ExportScope Scope { get; set; } = ExportScope.All;
    public string? ErrorMessage { get; set; }
}
