namespace HelpDeskHero.Api.BackgroundJobs.Contracts;

public interface IDemoCleanupJob
{
    Task CleanupExpiredDemoDataAsync(
        CancellationToken ct = default);
}