namespace TixeNex.Worker.Models;

public sealed class StoredExportFile
{
    public string ObjectName { get; init; } = default!;

    public string FileName { get; init; } = default!;

    public string ContentType { get; init; } = default!;

    public long SizeBytes { get; init; }
}