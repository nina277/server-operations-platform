using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IIncidentNoteRepository
{
    Task<List<IncidentNote>> GetForIncidentAsync(long incidentId, CancellationToken ct = default);

    Task AddAsync(IncidentNote note, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IMaintenanceWindowRepository
{
    Task<MaintenanceWindow?> FindByIdAsync(long id, CancellationToken ct = default);

    /// <summary>取り消していない、終了時刻が指定時刻以降の期間を返す(進行中と予定)。</summary>
    Task<List<MaintenanceWindow>> GetUpcomingAsync(DateTime asOf, CancellationToken ct = default);

    /// <summary>
    /// 指定時刻・指定対象で効いている期間を返す。
    /// 対象を限定しない期間(TargetIdがnull)も含める。
    /// </summary>
    Task<List<MaintenanceWindow>> GetActiveForTargetAsync(
        long targetId, DateTime asOf, CancellationToken ct = default);

    Task AddAsync(MaintenanceWindow window, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>同一署名の障害が過去に何回起きたかと、前回どう解決したか。</summary>
public record RecurrenceSummary
{
    public required int TotalCount { get; init; }

    public required int ResolvedCount { get; init; }

    public DateTime? FirstOccurredAt { get; init; }

    public DateTime? PreviousOccurredAt { get; init; }

    /// <summary>前回この障害を解決したときに成功した復旧操作のID。</summary>
    public string? LastSuccessfulActionId { get; init; }

    public DateTime? LastSuccessfulAt { get; init; }
}

/// <summary>運用実績の集計。期間を指定して評価指標を取り出す。</summary>
public record OperationsInsightsRange(DateTime From, DateTime To);

public interface IOperationsInsightsRepository
{
    /// <summary>インシデントの検知から最初の通知までの秒数(通知が作られたものだけ)。</summary>
    Task<List<double>> GetDetectionToNotificationSecondsAsync(
        OperationsInsightsRange range, CancellationToken ct = default);

    /// <summary>復旧操作の要求から完了までの秒数(完了したものだけ)。</summary>
    Task<List<double>> GetRecoveryDurationSecondsAsync(
        OperationsInsightsRange range, bool automaticOnly, CancellationToken ct = default);

    /// <summary>復旧操作の状態ごとの件数。</summary>
    Task<Dictionary<RecoveryActionStatus, int>> CountRecoveryByStatusAsync(
        OperationsInsightsRange range, bool automaticOnly, CancellationToken ct = default);

    /// <summary>自動実行を止めた理由ごとの件数。安全機構が何を止めたかを見る。</summary>
    Task<Dictionary<string, int>> CountBlockedReasonsAsync(
        OperationsInsightsRange range, CancellationToken ct = default);

    /// <summary>期間内に検知したインシデントの重大度ごとの件数。</summary>
    Task<Dictionary<IncidentSeverity, int>> CountIncidentsBySeverityAsync(
        OperationsInsightsRange range, CancellationToken ct = default);

    /// <summary>期間内に検知したインシデント数と、そのうち解決したもの。</summary>
    Task<(int Detected, int Resolved)> CountIncidentOutcomesAsync(
        OperationsInsightsRange range, CancellationToken ct = default);

    /// <summary>同一署名の障害の再発状況。</summary>
    Task<RecurrenceSummary> GetRecurrenceAsync(
        long targetId, string signature, long excludeIncidentId, CancellationToken ct = default);
}
