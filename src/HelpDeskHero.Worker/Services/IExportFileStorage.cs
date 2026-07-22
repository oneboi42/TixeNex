using HelpDeskHero.Worker.Models;

namespace HelpDeskHero.Worker.Services;

public interface IExportFileStorage
{
    Task<StoredExportFile> SaveAsync(
        Stream content,
        string objectName,
        string contentType,
        CancellationToken cancellationToken = default);
}