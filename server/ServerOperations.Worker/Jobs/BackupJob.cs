using Hangfire;
using ServerOperations.Core.Services.Backup;

namespace ServerOperations.Worker.Jobs;

/// <summary>定期バックアップ。失敗はBackupService側でHigh通知される。</summary>
public class BackupJob(IBackupService backupService, ILogger<BackupJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        var run = await backupService.RunAsync(triggeredByUserId: null, ct);
        logger.LogInformation("Scheduled backup finished with status {Status}.", run.Status);
    }
}

/// <summary>定期バックアップジョブを登録する。</summary>
public class BackupJobScheduler(
    IRecurringJobManager recurringJobs,
    IConfiguration configuration,
    ILogger<BackupJobScheduler> logger) : IHostedService
{
    private const string JobId = "scheduled-backup";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var cron = configuration.GetValue("Backup:Cron", "0 2 * * *")!;

        recurringJobs.AddOrUpdate<BackupJob>(
            JobId, "default", job => job.RunAsync(CancellationToken.None), cron);

        logger.LogInformation("Registered backup job with cron '{Cron}'.", cron);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
