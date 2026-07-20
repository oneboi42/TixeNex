using HelpDeskHero.Api.Infrastructure.Storage;
using Microsoft.AspNetCore.Http;

namespace HelpDeskHero.Worker.Services;

public class WorkerFileStorage : IFileStorage
{
    private readonly string _exportsPath;

    public WorkerFileStorage()
    {
        // Создаём папку exports в корне запуска воркера
        _exportsPath = Path.Combine(Directory.GetCurrentDirectory(), "exports");
        Directory.CreateDirectory(_exportsPath);
    }

    public async Task<StoredFileResult> SaveAsync(IFormFile file, CancellationToken ct = default)
{
    var filePath = Path.Combine(_exportsPath, file.FileName);

    using (var stream = new FileStream(filePath, FileMode.Create))
    {
        await file.CopyToAsync(stream, ct);
    }

    return new StoredFileResult
    {
        OriginalFileName = file.FileName,
        StoredFileName = file.FileName,
        RelativePath = Path.Combine("exports", file.FileName),
        ContentType = file.ContentType ?? "text/csv",
        SizeBytes = file.Length
    };
}

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_exportsPath, relativePath);
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"File not found: {relativePath}");
        }

        Stream stream = File.OpenRead(filePath);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_exportsPath, relativePath);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }
}