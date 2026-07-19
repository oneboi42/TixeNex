using HelpDeskHero.Api.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace HelpDeskHero.Api.Infrastructure.Services;

public class FakeMessagePublisher : IMessagePublisher
{
    private readonly ILogger<FakeMessagePublisher> _logger;

    public FakeMessagePublisher(ILogger<FakeMessagePublisher> logger)
    {
        _logger = logger;
    }

    public Task PublishAsync<T>(T message) where T : class
    {
        _logger.LogInformation("Publishing message to queue: {@Message}", message);
        return Task.CompletedTask;
    }
}