using Hangfire;

namespace ServerOperations.Worker.Jobs;

/// <summary>保持データ削除の定期ジョブを登録する。</summary>
public class RetentionJobScheduler(
    IRecurringJobManager recurringJobs,
    IConfiguration configuration,
    ILogger<RetentionJobScheduler> logger) : IHostedService
{
    private const string JobId = "retention-cleanup";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cron = configuration.GetValue("Retention:Cron", "0 3 * * *")!;

        recurringJobs.AddOrUpdate<RetentionCleanupJob>(
            JobId,
            "default",
            job => job.RunAsync(CancellationToken.None),
            cron);

        logger.LogInformation("Registered retention cleanup job with cron '{Cron}'.", cron);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
