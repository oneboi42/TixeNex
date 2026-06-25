namespace HelpDeskHero.Api.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;

    public LocalFileStorage(IWebHostEnvironment environment, IConfiguration configuration)
    {
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<StoredFileResult> SaveAsync(IFormFile file, CancellationToken ct = default)
    {
        var root = GetRootPath();
        Directory.CreateDirectory(root);

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedFileName = $"{Guid.NewGuid():N}{extension}";
        var path = Path.Combine(root, storedFileName);

        await using var stream = File.Create(path);
        await file.CopyToAsync(stream, ct);

        return new StoredFileResult
        {
            OriginalFileName = Path.GetFileName(file.FileName),
            StoredFileName = storedFileName,
            RelativePath = storedFileName,
            ContentType = string.IsNullOrWhiteSpace(file.ContentType)
                ? "application/octet-stream"
                : file.ContentType,
            SizeBytes = file.Length
        };
    }

    public Task<Stream> OpenReadAsync(string relativePath, CancellationToken ct = default)
    {
        var path = GetSafePath(relativePath);
        Stream stream = File.OpenRead(path);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var path = GetSafePath(relativePath);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetRootPath()
    {
        return _configuration["FileStorage:RootPath"]
            ?? Path.Combine(_environment.ContentRootPath, "App_Data", "attachments");
    }

    private string GetSafePath(string relativePath)
    {
        var root = Path.GetFullPath(GetRootPath());
        var path = Path.GetFullPath(Path.Combine(root, relativePath));

        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid file path.");

        return path;
    }
}
