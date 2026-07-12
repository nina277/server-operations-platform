using Hangfire;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Worker.Jobs;

/// <summary>
/// 対象別の定期収集ジョブ(Hangfire RecurringJob)を維持する。
/// 定期的に監視対象一覧と突き合わせ、有効な対象のジョブを登録し、無効・削除された対象のジョブを外す。
/// </summary>
public class CollectionJobScheduler(
    IServiceScopeFactory scopeFactory,
    IRecurringJobManager recurringJobs,
    IConfiguration configuration,
    ILogger<CollectionJobScheduler> logger) : BackgroundService
{
    private const string JobIdPrefix = "collect-target-";

    private readonly HashSet<string> _registered = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var syncInterval = TimeSpan.FromSeconds(
            Math.Clamp(configuration.GetValue("Collection:SyncIntervalSeconds", 60), 15, 3600));
        var collectionCron = configuration.GetValue("Collection:Cron", "* * * * *")!;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncJobsAsync(collectionCron, stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to sync collection jobs.");
            }

            try
            {
                await Task.Delay(syncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task SyncJobsAsync(string cron, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var targets = scope.ServiceProvider.GetRequiredService<IMonitoringTargetRepository>();
        var all = await targets.GetAllAsync(ct);

        var desired = all.Where(t => t.IsEnabled)
            .ToDictionary(t => $"{JobIdPrefix}{t.Id}", t => t.Id);

        foreach (var (jobId, targetId) in desired)
        {
            if (_registered.Add(jobId))
            {
                recurringJobs.AddOrUpdate<ITargetCollectionService>(
                    jobId,
                    "collection",
                    service => service.CollectAsync(targetId, CancellationToken.None),
                    cron);
                logger.LogInformation("Registered collection job for target {TargetId}", targetId);
            }
        }

        foreach (var jobId in _registered.Where(id => !desired.ContainsKey(id)).ToList())
        {
            recurringJobs.RemoveIfExists(jobId);
            _registered.Remove(jobId);
            logger.LogInformation("Removed collection job {JobId}", jobId);
        }
    }
}
