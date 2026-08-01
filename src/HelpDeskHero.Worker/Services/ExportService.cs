using System.Globalization;
using System.Text;
using CsvHelper;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Worker.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDeskHero.Worker.Services;

public interface IExportService
{
    Task ProcessExportAsync(
        Guid jobId,
        CancellationToken cancellationToken);
}

public sealed class ExportService : IExportService
{
    private readonly AppDbContext _db;
    private readonly IExportFileStorage _fileStorage;
    private readonly ILogger<ExportService> _logger;

    public ExportService(
        AppDbContext db,
        IExportFileStorage fileStorage,
        ILogger<ExportService> logger)
    {
        _db = db;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    public async Task ProcessExportAsync(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var job = await _db.ExportJobs
            .FirstOrDefaultAsync(
                exportJob => exportJob.Id == jobId,
                cancellationToken);

        if (job is null)
        {
            _logger.LogWarning(
                "Export job {JobId} was not found.",
                jobId);

            return;
        }

        var metadataError = ValidateMetadata(job);

        if (metadataError is not null)
        {
            _logger.LogError(
                "Export job {JobId} has unsupported metadata: {Reason}",
                jobId,
                metadataError);

            job.Status = ExportStatus.Failed;
            job.CompletedAt = null;
            job.FileName = null;
            job.StorageObjectName = null;
            job.ErrorMessage = "Unsupported export configuration.";

            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            job.Status = ExportStatus.Running;
            job.CompletedAt = null;
            job.FileName = null;
            job.StorageObjectName = null;
            job.ErrorMessage = null;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Export job {JobId} changed to Running.",
                jobId);

            var ticketQuery = _db.Tickets
                .AsNoTracking()
                .Where(ticket => !ticket.IsDeleted);

            ticketQuery = job.Scope switch
            {
                ExportScope.Own => ticketQuery.Where(
                    ticket => ticket.RequesterUserId == job.UserId),
                ExportScope.Assigned => ticketQuery.Where(
                    ticket => ticket.AssignedToUserId == job.UserId),
                ExportScope.All => ticketQuery,
                _ => throw new InvalidOperationException(
                    "Export scope was validated before query construction.")
            };

            var rows = await ticketQuery
                .Select(ticket => new TicketExportRow
                {
                    Id = ticket.Id,
                    Title = ticket.Title,
                    Status = ticket.Status,
                    CreatedAt = ticket.CreatedAtUtc
                })
                .ToListAsync(cancellationToken);
            var fileName =
                $"tickets_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";

            var safeUserId = Uri.EscapeDataString(job.UserId);

            var objectName =
                $"tickets/{safeUserId}/{job.Id:N}.csv";

            await using var memoryStream = new MemoryStream();

            await WriteCsvAsync(
                memoryStream,
                rows,
                cancellationToken);

            memoryStream.Position = 0;

            var storedFile = await _fileStorage.SaveAsync(
                content: memoryStream,
                objectName: objectName,
                contentType: "text/csv",
                cancellationToken: cancellationToken);


            job.Status = ExportStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.FileName = fileName;
            job.StorageObjectName = storedFile.ObjectName;
            job.ErrorMessage = null;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Export job {JobId} completed. FileName: {FileName}, ObjectName: {ObjectName}, Size: {SizeBytes} bytes.",
                jobId,
                fileName,
                storedFile.ObjectName,
                storedFile.SizeBytes);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Export job {JobId} was cancelled because the Worker is stopping.",
                jobId);

            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Failed to process export job {JobId}.",
                jobId);

            job.Status = ExportStatus.Failed;
            job.CompletedAt = null;
            job.FileName = null;
            job.StorageObjectName = null;
            job.ErrorMessage = "The export could not be completed.";

            try
            {
                await _db.SaveChangesAsync(CancellationToken.None);
            }
            catch (Exception databaseException)
            {
                _logger.LogError(
                    databaseException,
                    "Could not update export job {JobId} to Failed.",
                    jobId);
            }
        }
    }

    private static string? ValidateMetadata(ExportJob job)
    {
        if (job.ResourceType != ExportResourceType.Tickets)
            return $"ResourceType value {(int)job.ResourceType} is not supported.";

        if (job.Format != ExportFormat.Csv)
            return $"Format value {(int)job.Format} is not supported.";

        if (job.Scope is not ExportScope.Own and
            not ExportScope.Assigned and
            not ExportScope.All)
        {
            return $"Scope value {(int)job.Scope} is not supported.";
        }

        return null;
    }

    private static async Task WriteCsvAsync(
        Stream targetStream,
        IEnumerable<TicketExportRow> rows,
        CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(
            stream: targetStream,
            encoding: new UTF8Encoding(
                encoderShouldEmitUTF8Identifier: true),
            bufferSize: 1024,
            leaveOpen: true);

        using var csv = new CsvWriter(
            writer,
            CultureInfo.InvariantCulture);

        await csv.WriteRecordsAsync(
            rows,
            cancellationToken);

        await writer.FlushAsync(cancellationToken);
    }
}
