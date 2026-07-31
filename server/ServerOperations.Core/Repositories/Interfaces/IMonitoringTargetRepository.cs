using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IMonitoringTargetRepository
{
    /// <summary>Profile・Credentialsを含めて取得する。</summary>
    Task<MonitoringTarget?> FindByIdAsync(long id, CancellationToken ct = default);

    Task<MonitoringTarget?> FindByNameAsync(string name, CancellationToken ct = default);

    Task<List<MonitoringTarget>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(MonitoringTarget target, CancellationToken ct = default);

    /// <summary>削除したときに一緒に消えるものの件数。削除前に画面で示す。</summary>
    Task<TargetDependents> CountDependentsAsync(long targetId, CancellationToken ct = default);

    /// <summary>
    /// 対象と、それに紐づく収集値・インシデント・復旧履歴をまとめて削除する。
    ///
    /// これらのテーブルは対象への外部キーを持たないため、対象だけを消すと
    /// 参照先の無い行が黙って残る。明示的に消す必要がある。
    /// 監査ログは対象を文字列で持つため消さない(誰が何をしたかの記録は残す)。
    /// </summary>
    Task DeleteWithDependentsAsync(MonitoringTarget target, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>対象を削除したときに一緒に消えるものの件数。</summary>
public record TargetDependents
{
    public required int MetricSnapshots { get; init; }

    public required int Incidents { get; init; }

    public required int IncidentLogs { get; init; }

    public required int Diagnoses { get; init; }

    public required int RecoveryActions { get; init; }

    public required int HealthChecks { get; init; }

    public required int Notifications { get; init; }

    public required int MaintenanceWindows { get; init; }

    public int Total =>
        MetricSnapshots + Incidents + IncidentLogs + Diagnoses
        + RecoveryActions + HealthChecks + Notifications + MaintenanceWindows;
}
