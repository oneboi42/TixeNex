using System.Globalization;
using CsvHelper;
using HelpDeskHero.Api.Domain;
using HelpDeskHero.Api.Infrastructure.Persistence;
using HelpDeskHero.Worker.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HelpDeskHero.Worker.Services;

public interface IExportService
{
    Task ProcessExportAsync(Guid jobId, CancellationToken cancellationToken);
}

public class ExportService : IExportService
{
    private readonly AppDbContext _db;
    private readonly ILogger<ExportService> _logger;

    public ExportService(AppDbContext db, ILogger<ExportService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task ProcessExportAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _db.ExportJobs.FirstOrDefaultAsync(x => x.Id == jobId, cancellationToken);
        if (job == null)
        {
            _logger.LogWarning("Export job {JobId} not found", jobId);
            return;
        }

        try
        {
            job.Status = ExportStatus.Running;
            await _db.SaveChangesAsync(cancellationToken);

            var tickets = await _db.Tickets
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var rows = tickets.Select(t => new TicketExportRow
            {
                Id = t.Id, 
                Title = t.Title,
                Status = t.Status.ToString(),
                CreatedAt = t.CreatedAtUtc
            }).ToList();

            var exportsFolder = Path.Combine(Directory.GetCurrentDirectory(), "exports");
            Directory.CreateDirectory(exportsFolder);

            var fileName = $"tickets_{DateTime.UtcNow:yyyyMMdd_HHmmss}.csv";
            var filePath = Path.Combine(exportsFolder, fileName);

            using (var writer = new StreamWriter(filePath))
            using (var csv = new CsvWriter(writer, CultureInfo.InvariantCulture))
            {
                await csv.WriteRecordsAsync(rows, cancellationToken);
            }

            job.Status = ExportStatus.Completed;
            job.CompletedAt = DateTime.UtcNow; 
            job.FileName = fileName;

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Export job {JobId} completed successfully", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process export job {JobId}", jobId);

            job.Status = ExportStatus.Failed;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}