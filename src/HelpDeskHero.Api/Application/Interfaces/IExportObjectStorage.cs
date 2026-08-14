namespace HelpDeskHero.Api.Application.Interfaces;

public interface IExportObjectStorage
{
    Task<byte[]> DownloadAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        string objectName,
        CancellationToken cancellationToken = default);
}