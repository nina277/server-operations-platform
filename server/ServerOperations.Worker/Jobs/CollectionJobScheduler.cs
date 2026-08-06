using Hangfire;
using ServerOperations.Core.Models.Operations;
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

    /// <summary>登録済みジョブID → 適用中のcron式。間隔の変更を見分けるために保持する。</summary>
    private readonly Dictionary<string, string> _registered = [];

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

    private async Task SyncJobsAsync(string defaultCron, CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var targets = scope.ServiceProvider.GetRequiredService<IMonitoringTargetRepository>();
        var all = await targets.GetAllAsync(ct);

        var desired = all.Where(t => t.IsEnabled)
            .ToDictionary(
                t => $"{JobIdPrefix}{t.Id}",
                t => (TargetId: t.Id, Cron: CronFor(t, defaultCron)));

        foreach (var (jobId, plan) in desired)
        {
            // 間隔が変わった対象は登録し直す。こうしないと、画面で間隔を変えても
            // Workerを再起動するまで古い間隔のまま動き続ける。
            if (_registered.TryGetValue(jobId, out var currentCron) && currentCron == plan.Cron)
            {
                continue;
            }

            var targetId = plan.TargetId;
            // キューは CollectionJob の [Queue] 属性で決まる。
            // ここで渡す形は MySqlStorage が対応していない
            recurringJobs.AddOrUpdate<CollectionJob>(
                jobId,
                job => job.RunAsync(targetId, CancellationToken.None),
                plan.Cron);

            logger.LogInformation(
                "Registered collection job for target {TargetId} with cron {Cron}", targetId, plan.Cron);
            _registered[jobId] = plan.Cron;
        }

        foreach (var jobId in _registered.Keys.Where(id => !desired.ContainsKey(id)).ToList())
        {
            recurringJobs.RemoveIfExists(jobId);
            _registered.Remove(jobId);
            logger.LogInformation("Removed collection job {JobId}", jobId);
        }
    }

    /// <summary>
    /// 対象ごとの収集間隔をcronへ直す。未設定の対象は全体の既定値を使う。
    /// </summary>
    private static string CronFor(MonitoringTarget target, string defaultCron) =>
        target.CollectionIntervalSeconds is { } seconds
            ? CollectionInterval.ToCron(seconds)
            : defaultCron;
}
