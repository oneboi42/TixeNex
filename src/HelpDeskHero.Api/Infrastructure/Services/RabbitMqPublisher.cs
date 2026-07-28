using System.Text;
using System.Text.Json;
using HelpDeskHero.Api.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace HelpDeskHero.Api.Infrastructure.Services;

public class RabbitMqPublisher : IMessagePublisher
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RabbitMqPublisher> _logger;

    public RabbitMqPublisher(IConfiguration configuration, ILogger<RabbitMqPublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task PublishAsync<T>(T message) where T : class
    {
        var factory = new ConnectionFactory
        {
            HostName = _configuration["RabbitMQ:Host"] ?? "localhost",

            UserName =
                _configuration["RabbitMQ:UserName"]
                ?? "guest",

            Password =
                _configuration["RabbitMQ:Password"]
                ?? "guest"
        };

        using var connection = await factory.CreateConnectionAsync();
        using var channel = await connection.CreateChannelAsync();

        await channel.QueueDeclareAsync(
            queue: "export_jobs",
            durable: true,
            exclusive: false,
            autoDelete: false);

        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        await channel.BasicPublishAsync(
            exchange: string.Empty,
            routingKey: "export_jobs",
            body: body);

        _logger.LogInformation("Successfully published message to RabbitMQ: {Json}", json);
    }
}