using TixeNex.Worker.Models;

namespace TixeNex.Worker.Services;

public sealed class WorkerFileStorage : IExportFileStorage
{
    private readonly string _exportsPath;

    public WorkerFileStorage()
    {
        _exportsPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "exports");

        Directory.CreateDirectory(_exportsPath);
    }

    public async Task<StoredExportFile> SaveAsync(
        Stream content,
        string objectName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        if (content is null)
        {
            throw new ArgumentNullException(nameof(content));
        }

        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new ArgumentException(
                "Object name cannot be empty.",
                nameof(objectName));
        }

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException(
                "Content type cannot be empty.",
                nameof(contentType));
        }

        var safeObjectName = objectName
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var filePath = Path.Combine(_exportsPath, safeObjectName);

        var directoryPath = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        if (content.CanSeek)
        {
            content.Position = 0;
        }

        await using var fileStream = new FileStream(
            path: filePath,
            mode: FileMode.Create,
            access: FileAccess.Write,
            share: FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        await content.CopyToAsync(
            fileStream,
            cancellationToken);

        await fileStream.FlushAsync(cancellationToken);

        var fileName = Path.GetFileName(safeObjectName);

        return new StoredExportFile
        {
            ObjectName = objectName,
            FileName = fileName,
            ContentType = contentType,
            SizeBytes = fileStream.Length
        };
    }
}