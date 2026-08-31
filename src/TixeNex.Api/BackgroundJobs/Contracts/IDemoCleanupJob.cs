using Hangfire;

namespace TixeNex.Api.BackgroundJobs.Contracts;

public interface IDemoCleanupJob
{
    [DisableConcurrentExecution(300)]
    Task CleanupExpiredDemoDataAsync(
        CancellationToken ct = default);

    [DisableConcurrentExecution(300)]
    Task ResetSeededDemoTicketsAsync(
        CancellationToken ct = default);
}
