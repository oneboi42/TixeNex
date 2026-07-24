using System.Text;
using System.Text.Json;
using HelpDeskHero.Worker.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace HelpDeskHero.Worker.Messaging;

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
            var body = ea.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);

            _logger.LogInformation("RECEIVED MESSAGE FROM RABBITMQ: {Json}", json);

            try
            {
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("ExportJobId", out var idProp) ||
                    doc.RootElement.TryGetProperty("id", out idProp) ||
                    doc.RootElement.TryGetProperty("jobId", out idProp) ||
                    doc.RootElement.TryGetProperty("JobId", out idProp))
                {
                    var rawVal = idProp.GetString();
                    if (Guid.TryParse(rawVal, out var jobId))
                    {
                        _logger.LogInformation("Processing export job {JobId}...", jobId);

                        using var scope = _scopeFactory.CreateScope();
                        var exportService = scope.ServiceProvider.GetRequiredService<IExportService>();

                        await exportService.ProcessExportAsync(jobId, stoppingToken);
                    }
                    else
                    {
                        _logger.LogWarning("Could not parse Guid from property value: {Value}", rawVal);
                    }
                }
                else
                {
                    _logger.LogWarning("Target Id property not found in JSON payload");
                }

                await channel.BasicAckAsync(deliveryTag: ea.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing RabbitMQ message");
            }
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