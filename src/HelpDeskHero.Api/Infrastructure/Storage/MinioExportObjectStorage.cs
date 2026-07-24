using HelpDeskHero.Api.Application.Interfaces;
using Microsoft.Extensions.Options;
using Minio;
using Minio.DataModel.Args;

namespace HelpDeskHero.Api.Infrastructure.Storage;

public sealed class MinioExportObjectStorage
    : IExportObjectStorage
{
    private readonly IMinioClient _minioClient;
    private readonly MinioOptions _options;
    private readonly ILogger<MinioExportObjectStorage> _logger;

    public MinioExportObjectStorage(
        IMinioClient minioClient,
        IOptions<MinioOptions> options,
        ILogger<MinioExportObjectStorage> logger)
    {
        _minioClient = minioClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<byte[]> DownloadAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            throw new ArgumentException(
                "Object name cannot be empty.",
                nameof(objectName));
        }

        await using var outputStream = new MemoryStream();

        var getObjectArgs = new GetObjectArgs()
            .WithBucket(_options.BucketName)
            .WithObject(objectName)
            .WithCallbackStream(
                async (sourceStream, callbackCancellationToken) =>
                {
                    await sourceStream.CopyToAsync(
                        outputStream,
                        callbackCancellationToken);
                });

        await _minioClient.GetObjectAsync(
            getObjectArgs,
            cancellationToken);

        _logger.LogInformation(
            "Downloaded export object {ObjectName} from MinIO bucket {BucketName}.",
            objectName,
            _options.BucketName);

        return outputStream.ToArray();
    }
}