using System.Text.Json;
using TixeNex.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace TixeNex.Worker.Messaging;

public class RabbitMqConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqConsumer> _logger;

    public RabbitMqConsumer(
        IServiceScopeFactory scopeFactory, 
        IConfiguration configuration,
        ILogger<RabbitMqConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = 
                _configuration["RabbitMQ:Host"]
                 ?? "localhost",

            Port =
                _configuration.GetValue<int?>("RabbitMQ:Port")
                ?? 5672,

            UserName =
                _configuration["RabbitMQ:UserName"]
                ?? "guest",

            Password =
                _configuration["RabbitMQ:Password"]
                ?? "guest",

            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
        };

        using var connection = await factory.CreateConnectionAsync(stoppingToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);

        await channel.QueueDeclareAsync(
            queue: "export_jobs", 
            durable: true, 
            exclusive: false, 
            autoDelete: false, 
            cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        
        consumer.ReceivedAsync += async (model, ea) =>
        {
            await ExportMessageHandler.HandleAsync(
                ea.Body,
                async jobId =>
                {
                    using var scope = _scopeFactory.CreateScope();
                    var exportService = scope.ServiceProvider
                        .GetRequiredService<IExportService>();

                    await exportService.ProcessExportAsync(
                        jobId,
                        stoppingToken);
                },
                acknowledge: () => channel.BasicAckAsync(
                    deliveryTag: ea.DeliveryTag,
                    multiple: false,
                    cancellationToken: CancellationToken.None).AsTask(),
                retry: () => channel.BasicNackAsync(
                    deliveryTag: ea.DeliveryTag,
                    multiple: false,
                    requeue: true,
                    cancellationToken: CancellationToken.None).AsTask(),
                _logger);
        };

        await channel.BasicConsumeAsync(
            queue: "export_jobs", 
            autoAck: false, 
            consumer: consumer, 
            cancellationToken: stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(1000, stoppingToken);
        }
    }
}

internal static class ExportMessageHandler
{
    internal static async Task HandleAsync(
        ReadOnlyMemory<byte> body,
        Func<Guid, Task> process,
        Func<Task> acknowledge,
        Func<Task> retry,
        ILogger logger)
    {
        if (!TryParseJobId(body, out var jobId))
        {
            logger.LogWarning(
                "Discarding unusable export message and acknowledging it.");
            await acknowledge();
            return;
        }

        try
        {
            logger.LogInformation(
                "Processing export job {JobId}...",
                jobId);
            await process(jobId);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Error processing export job {JobId}; message will be retried.",
                jobId);
            await retry();
            return;
        }

        await acknowledge();
    }

    internal static bool TryParseJobId(
        ReadOnlyMemory<byte> body,
        out Guid jobId)
    {
        jobId = default;

        if (body.IsEmpty)
            return false;

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetIdProperty(root, out var idProperty) ||
                idProperty.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            return Guid.TryParse(idProperty.GetString(), out jobId);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetIdProperty(
        JsonElement root,
        out JsonElement idProperty) =>
        root.TryGetProperty("ExportJobId", out idProperty) ||
        root.TryGetProperty("id", out idProperty) ||
        root.TryGetProperty("jobId", out idProperty) ||
        root.TryGetProperty("JobId", out idProperty);
}
