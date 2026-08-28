using TixeNex.Worker.Models;

namespace TixeNex.Worker.Services;

public interface IExportFileStorage
{
    Task<StoredExportFile> SaveAsync(
        Stream content,
        string objectName,
        string contentType,
        CancellationToken cancellationToken = default);
}