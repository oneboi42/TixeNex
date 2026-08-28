using TixeNex.Api.Infrastructure.Persistence;
using TixeNex.Worker.Configuration;
using TixeNex.Worker.Messaging;
using TixeNex.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Minio;

var builder = Host.CreateApplicationBuilder(args);

var connectionString =
    builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' was not found.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

builder.Services
    .AddOptions<MinioOptions>()
    .Bind(
        builder.Configuration.GetSection(
            MinioOptions.SectionName))
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.Endpoint),
        "Minio:Endpoint is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.AccessKey),
        "Minio:AccessKey is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.SecretKey),
        "Minio:SecretKey is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.BucketName),
        "Minio:BucketName is required.")
    .ValidateOnStart();

builder.Services.AddSingleton<IMinioClient>(serviceProvider =>
{
    var options = serviceProvider
        .GetRequiredService<IOptions<MinioOptions>>()
        .Value;

    return new MinioClient()
        .WithEndpoint(options.Endpoint)
        .WithCredentials(
            options.AccessKey,
            options.SecretKey)
        .WithSSL(options.UseSsl)
        .Build();
});

builder.Services.AddScoped<
    IExportFileStorage,
    MinioExportFileStorage>();

/*
builder.Services.AddScoped<
    IExportFileStorage,
    WorkerFileStorage>();
*/

builder.Services.AddScoped<
    IExportService,
    ExportService>();

builder.Services.AddHostedService<RabbitMqConsumer>();

var host = builder.Build();

await host.RunAsync();