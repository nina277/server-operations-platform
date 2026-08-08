using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Data;

namespace ServerOperations.Core.Services;

/// <summary>保持プロファイル。個別設定(custom)以外は既定値を使う。</summary>
public record RetentionPolicy(int MetricsDays, int LogsDays, int IncidentsDays, int AuditDays)
{
    /// <summary>
    /// 監査ログの保持日数の下限。
    ///
    /// 監査ログは改ざん・削除の証跡そのもので、他のデータとは意味が違う。
    /// 管理者権限を取られた場合、保持期間を1日に縮めるだけで痕跡を消せてしまう。
    /// 保持設定の変更自体は監査に残るが、その記録も同じ設定で消える対象になる。
    /// そのため、どの経路から設定しても下回れないようにする。
    /// </summary>
    public const int MinAuditDays = 90;

    /// <summary>下限を割る指定は下限へ引き上げる。画面からの入力は先に拒否される。</summary>
    public int AuditDays { get; init; } = Math.Max(AuditDays, MinAuditDays);

    public static RetentionPolicy Compact => new(7, 7, 90, 90);

    public static RetentionPolicy Standard => new(30, 30, 365, 365);

    public static RetentionPolicy LongTerm => new(180, 180, 1095, 1095);

    /// <summary>プロファイル名から既定値を解決する。customは呼び出し側の個別値を使う。</summary>
    public static RetentionPolicy FromProfile(string profile) => profile switch
    {
        "compact" => Compact,
        "long-term" => LongTerm,
        "standard" => Standard,
        _ => Standard,
    };
}

/// <summary>削除対象の件数見込み。削除前に画面で確認できるようにする。</summary>
public record RetentionPreview(
    int MetricSnapshots,
    int IncidentLogs,
    int Incidents,
    int AuditLogs,
    int Notifications,
    int HealthChecks)
{
    public int Total => MetricSnapshots + IncidentLogs + Incidents + AuditLogs + Notifications + HealthChecks;

    /// <summary>おおよその容量見込み(バイト)。1行あたりの平均サイズから概算する。</summary>
    public long EstimatedBytes =>
        (long)MetricSnapshots * 2048
        + (long)IncidentLogs * 4096
        + (long)Incidents * 512
        + (long)AuditLogs * 768
        + (long)Notifications * 512
        + (long)HealthChecks * 256;
}

public interface IRetentionService
{
    /// <summary>削除予定件数と容量見込みを返す(削除は行わない)。</summary>
    Task<RetentionPreview> PreviewAsync(RetentionPolicy policy, CancellationToken ct = default);

    /// <summary>保持期間を超えたデータを削除し、削除件数を返す。</summary>
    Task<RetentionPreview> PurgeAsync(RetentionPolicy policy, CancellationToken ct = default);
}

public class RetentionService(
    AppDbContext db,
    TimeProvider timeProvider,
    ILogger<RetentionService> logger) : IRetentionService
{
    public async Task<RetentionPreview> PreviewAsync(RetentionPolicy policy, CancellationToken ct = default)
    {
        var cutoffs = Cutoffs(policy);

        return new RetentionPreview(
            MetricSnapshots: await db.MetricSnapshots.CountAsync(m => m.CollectedAt < cutoffs.Metrics, ct),
            IncidentLogs: await db.IncidentLogs.CountAsync(l => l.CollectedAt < cutoffs.Logs, ct),
            // 未解決のインシデントは保持期間を過ぎても削除しない
            Incidents: await db.Incidents.CountAsync(i =>
                i.LastOccurredAt < cutoffs.Incidents
                && (i.Status == Models.Operations.IncidentStatus.Resolved
                    || i.Status == Models.Operations.IncidentStatus.Closed), ct),
            AuditLogs: await db.AuditLogs.CountAsync(a => a.OccurredAt < cutoffs.Audit, ct),
            Notifications: await db.Notifications.CountAsync(n => n.LastNotifiedAt < cutoffs.Logs, ct),
            HealthChecks: await db.HealthChecks.CountAsync(h => h.CheckedAt < cutoffs.Metrics, ct));
    }

    public async Task<RetentionPreview> PurgeAsync(RetentionPolicy policy, CancellationToken ct = default)
    {
        var cutoffs = Cutoffs(policy);

        var metrics = await db.MetricSnapshots
            .Where(m => m.CollectedAt < cutoffs.Metrics).ExecuteDeleteAsync(ct);
        var logs = await db.IncidentLogs
            .Where(l => l.CollectedAt < cutoffs.Logs).ExecuteDeleteAsync(ct);
        var notifications = await db.Notifications
            .Where(n => n.LastNotifiedAt < cutoffs.Logs).ExecuteDeleteAsync(ct);
        var healthChecks = await db.HealthChecks
            .Where(h => h.CheckedAt < cutoffs.Metrics).ExecuteDeleteAsync(ct);

        // 未解決のインシデントは残す
        var incidents = await db.Incidents
            .Where(i => i.LastOccurredAt < cutoffs.Incidents
                && (i.Status == Models.Operations.IncidentStatus.Resolved
                    || i.Status == Models.Operations.IncidentStatus.Closed))
            .ExecuteDeleteAsync(ct);

        var audit = await db.AuditLogs
            .Where(a => a.OccurredAt < cutoffs.Audit).ExecuteDeleteAsync(ct);

        var result = new RetentionPreview(metrics, logs, incidents, audit, notifications, healthChecks);
        logger.LogInformation(
            "Retention purge removed {Total} rows (metrics={Metrics}, logs={Logs}, incidents={Incidents}, audit={Audit}).",
            result.Total, metrics, logs, incidents, audit);

        return result;
    }

    private (DateTime Metrics, DateTime Logs, DateTime Incidents, DateTime Audit) Cutoffs(RetentionPolicy policy)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return (
            now.AddDays(-policy.MetricsDays),
            now.AddDays(-policy.LogsDays),
            now.AddDays(-policy.IncidentsDays),
            now.AddDays(-policy.AuditDays));
    }
}
