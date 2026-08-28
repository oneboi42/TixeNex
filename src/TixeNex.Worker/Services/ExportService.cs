using System.Globalization;
using System.Text;
using CsvHelper;
using CsvHelper.TypeConversion;
using TixeNex.Api.Domain;
using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Worker.Models;
using Microsoft.EntityFrameworkCore;

namespace TixeNex.Worker.Services;

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
        var claimed = await ClaimPendingJobAsync(
            _db,
            jobId,
            cancellationToken);

        if (!claimed)
        {
            _logger.LogInformation(
                "Export job {JobId} was not found or is no longer Pending.",
                jobId);

            return;
        }

        var job = await _db.ExportJobs.SingleAsync(
            exportJob => exportJob.Id == jobId,
            cancellationToken);

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
            _logger.LogInformation(
                "Export job {JobId} changed to Running.",
                jobId);

            var exportOwner = await _db.Users
                .AsNoTracking()
                .Where(user => user.Id == job.UserId)
                .Select(user => new
                {
                    user.IsDemoWorkspace
                })
                .SingleOrDefaultAsync(cancellationToken);

            if (exportOwner is null)
            {
                throw new InvalidOperationException(
                    $"Export owner {job.UserId} was not found.");
            }

            var ticketQuery = _db.Tickets
                .AsNoTracking()
                .Where(ticket => !ticket.IsDeleted);

            ticketQuery = exportOwner.IsDemoWorkspace
                ? ticketQuery.Where(ticket => ticket.DemoExpiresAtUtc != null)
                : ticketQuery.Where(ticket => ticket.DemoExpiresAtUtc == null);

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
                .OrderByDescending(ticket => ticket.CreatedAtUtc)
                .ThenByDescending(ticket => ticket.Id)
                .Select(ticket => new TicketExportRow
                {
                    Id = ticket.Id,
                    TicketNumber = ticket.Number,
                    Title = ticket.Title,
                    Description = ticket.Description,
                    Status = ticket.Status,
                    Priority = ticket.Priority,
                    Requester = ticket.RequesterUser != null
                        ? ticket.RequesterUser.DisplayName
                        : string.Empty,
                    AssignedTo = _db.Users
                        .Where(user => user.Id == ticket.AssignedToUserId)
                        .Select(user => user.DisplayName)
                        .FirstOrDefault() ?? string.Empty,
                    CreatedAtUtc = ticket.CreatedAtUtc,
                    UpdatedAtUtc = ticket.UpdatedAtUtc,
                    FirstRespondedAtUtc = ticket.FirstRespondedAtUtc,
                    ResolvedAtUtc = ticket.ResolvedAtUtc,
                    DueFirstResponseAtUtc = ticket.DueFirstResponseAtUtc,
                    DueResolveAtUtc = ticket.DueResolveAtUtc
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

    internal static async Task<bool> ClaimPendingJobAsync(
        AppDbContext db,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        var updated = await db.ExportJobs
            .Where(job =>
                job.Id == jobId &&
                job.Status == ExportStatus.Pending)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(job => job.Status, ExportStatus.Running)
                    .SetProperty(job => job.CompletedAt, (DateTime?)null)
                    .SetProperty(job => job.FileName, (string?)null)
                    .SetProperty(job => job.StorageObjectName, (string?)null)
                    .SetProperty(job => job.ErrorMessage, (string?)null),
                cancellationToken);

        return updated == 1;
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

    internal static async Task WriteCsvAsync(
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

        csv.Context.TypeConverterCache.AddConverter<string>(
            new SpreadsheetSafeStringConverter());

        await csv.WriteRecordsAsync(
            rows,
            cancellationToken);

        await writer.FlushAsync(cancellationToken);
    }
}

internal sealed class SpreadsheetSafeStringConverter : DefaultTypeConverter
{
    public override string? ConvertToString(
        object? value,
        IWriterRow row,
        CsvHelper.Configuration.MemberMapData memberMapData)
    {
        var text = value as string ??
            base.ConvertToString(value, row, memberMapData);

        return CsvCellNeutralizer.Neutralize(text);
    }
}

internal static class CsvCellNeutralizer
{
    public static string? Neutralize(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
    }
}
