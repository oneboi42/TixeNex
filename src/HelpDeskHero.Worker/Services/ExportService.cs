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
                x => x.Id == jobId,
                cancellationToken);

        if (job is null)
        {
            _logger.LogWarning(
                "Export job {JobId} was not found.",
                jobId);

            return;
        }

        try
        {
            job.Status = ExportStatus.Running;
            job.CompletedAt = null;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Export job {JobId} changed to Running.",
                jobId);

            var tickets = await _db.Tickets
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var rows = tickets
                .Select(ticket => new TicketExportRow
                {
                    Id = ticket.Id,
                    Title = ticket.Title,
                    Status = ticket.Status.ToString(),
                    CreatedAt = ticket.CreatedAtUtc
                })
                .ToList();

            var fileName =
                $"tickets_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{job.Id:N}.csv";

            await using var memoryStream = new MemoryStream();

            await WriteCsvAsync(
                memoryStream,
                rows,
                cancellationToken);

            memoryStream.Position = 0;

            var storedFile = await _fileStorage.SaveAsync(
                content: memoryStream,
                objectName: fileName,
                contentType: "text/csv",
                cancellationToken: cancellationToken);

            job.Status = ExportStatus.Completed;
            job.CompletedAt = DateTime.UtcNow;
            job.FileName = storedFile.FileName;

            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Export job {JobId} completed. File: {FileName}, size: {SizeBytes} bytes.",
                jobId,
                storedFile.FileName,
                storedFile.SizeBytes);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation(
                "Export job {JobId} was cancelled because the worker is stopping.",
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

    private static async Task WriteCsvAsync(
        Stream targetStream,
        IEnumerable<TicketExportRow> rows,
        CancellationToken cancellationToken)
    {
        using var writer = new StreamWriter(
            stream: targetStream,
            encoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
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