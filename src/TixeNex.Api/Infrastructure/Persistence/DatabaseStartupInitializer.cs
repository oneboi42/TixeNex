using Microsoft.Data.SqlClient;

namespace TixeNex.Api.Infrastructure.Persistence;

public static class DatabaseStartupInitializer
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    public static async Task InitializeAsync(
        IServiceProvider services,
        IHostApplicationLifetime applicationLifetime,
        ILogger logger)
    {
        var attempt = 0;

        while (!applicationLifetime.ApplicationStopping.IsCancellationRequested)
        {
            try
            {
                await DbSeeder.SeedAsync(
                    services,
                    applicationLifetime.ApplicationStopping);
                return;
            }
            catch (OperationCanceledException)
                when (applicationLifetime.ApplicationStopping.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
                when (exception is SqlException or TimeoutException)
            {
                attempt++;
                logger.LogWarning(
                    exception,
                    "Database initialization attempt {Attempt} failed; retrying in {RetryDelaySeconds} seconds.",
                    attempt,
                    RetryDelay.TotalSeconds);

                await Task.Delay(
                    RetryDelay,
                    applicationLifetime.ApplicationStopping);
            }
        }
    }
}
