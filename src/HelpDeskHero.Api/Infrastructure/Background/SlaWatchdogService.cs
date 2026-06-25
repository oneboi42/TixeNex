using HelpDeskHero.Api.Application.Interfaces;

namespace HelpDeskHero.Api.Infrastructure.Background;

public sealed class SlaWatchdogService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SlaWatchdogService> _logger;

    public SlaWatchdogService(
        IServiceProvider serviceProvider,
        ILogger<SlaWatchdogService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var slaMonitor = scope.ServiceProvider.GetRequiredService<ISlaMonitorService>();

                await slaMonitor.CheckBreachesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SLA watchdog check failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }
}
