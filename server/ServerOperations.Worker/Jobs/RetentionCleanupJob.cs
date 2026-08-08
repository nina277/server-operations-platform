using System.Text.Json;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Worker.Jobs;

/// <summary>
/// 保持期間を超えたデータを削除する定期ジョブ。
/// 保持設定(SystemSetting)を読み、プロファイルまたは個別日数に従う。
/// </summary>
// キューは属性で指定する。AddOrUpdate に渡す形は MySqlStorage が対応していない
[Hangfire.Queue("default")]
public class RetentionCleanupJob(
    ISystemSettingRepository settings,
    IRetentionService retentionService,
    ILogger<RetentionCleanupJob> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task RunAsync(CancellationToken ct = default)
    {
        var policy = await ResolvePolicyAsync(ct);
        var result = await retentionService.PurgeAsync(policy, ct);

        logger.LogInformation(
            "Retention cleanup finished. removed={Total} (metrics={Metrics}, logs={Logs}, incidents={Incidents}, audit={Audit})",
            result.Total, result.MetricSnapshots, result.IncidentLogs, result.Incidents, result.AuditLogs);
    }

    private async Task<RetentionPolicy> ResolvePolicyAsync(CancellationToken ct)
    {
        var stored = await settings.FindByCategoryAsync(SettingCategory.Retention, ct);
        if (stored is null)
        {
            return RetentionPolicy.Standard;
        }

        var dto = JsonSerializer.Deserialize<StoredRetention>(stored.Value, JsonOptions);
        if (dto is null)
        {
            return RetentionPolicy.Standard;
        }

        return dto.Profile == "custom"
            ? new RetentionPolicy(dto.MetricsDays, dto.LogsDays, dto.IncidentsDays, dto.AuditDays)
            : RetentionPolicy.FromProfile(dto.Profile);
    }

    private sealed record StoredRetention(
        string Profile, int MetricsDays, int LogsDays, int IncidentsDays, int AuditDays);
}
