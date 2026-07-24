using HelpDeskHero.Worker.Configuration;
using HelpDeskHero.Worker.Models;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace HelpDeskHero.Worker.Services;

public sealed class MinioExportFileStorage : IExportFileStorage
{
    private static readonly SemaphoreSlim BucketCreationLock =
        new(initialCount: 1, maxCount: 1);

    private readonly IMinioClient _minioClient;
    private readonly MinioOptions _options;
    private readonly ILogger<MinioExportFileStorage> _logger;

    public MinioExportFileStorage(
        IMinioClient minioClient,
        IOptions<MinioOptions> options,
        ILogger<MinioExportFileStorage> logger)
    {
        _minioClient = minioClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<StoredExportFile> SaveAsync(
        Stream content,
        string objectName,
        string contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

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

        if (!content.CanSeek)
        {
            throw new InvalidOperationException(
                "The export stream must support seeking so its size can be determined.");
        }

        await EnsureBucketExistsAsync(cancellationToken);

        content.Position = 0;

        var objectSize = content.Length;

        var putObjectArgs = new PutObjectArgs()
            .WithBucket(_options.BucketName)
            .WithObject(objectName)
            .WithStreamData(content)
            .WithObjectSize(objectSize)
            .WithContentType(contentType);

        await _minioClient.PutObjectAsync(
            putObjectArgs,
            cancellationToken);

        _logger.LogInformation(
            "Uploaded export object {ObjectName} to MinIO bucket {BucketName}. Size: {SizeBytes} bytes.",
            objectName,
            _options.BucketName,
            objectSize);

        return new StoredExportFile
        {
            ObjectName = objectName,
            FileName = Path.GetFileName(objectName),
            ContentType = contentType,
            SizeBytes = objectSize
        };
    }

    private async Task EnsureBucketExistsAsync(
        CancellationToken cancellationToken)
    {
        var bucketExistsArgs = new BucketExistsArgs()
            .WithBucket(_options.BucketName);

        var bucketExists = await _minioClient.BucketExistsAsync(
            bucketExistsArgs,
            cancellationToken);

        if (bucketExists)
        {
            return;
        }

        await BucketCreationLock.WaitAsync(cancellationToken);

        try
        {
            bucketExists = await _minioClient.BucketExistsAsync(
                bucketExistsArgs,
                cancellationToken);

            if (bucketExists)
            {
                return;
            }

            var makeBucketArgs = new MakeBucketArgs()
                .WithBucket(_options.BucketName);

            await _minioClient.MakeBucketAsync(
                makeBucketArgs,
                cancellationToken);

            _logger.LogInformation(
                "Created MinIO bucket {BucketName}.",
                _options.BucketName);
        }
        finally
        {
            BucketCreationLock.Release();
        }
    }
}