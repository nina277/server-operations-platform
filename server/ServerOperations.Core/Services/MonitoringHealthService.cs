using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

/// <summary>監視が届いているかの状態。</summary>
public enum MonitoringReach
{
    /// <summary>直近の収集がある。</summary>
    Reaching = 0,

    /// <summary>一度も収集されていない(登録直後を除く)。</summary>
    NeverCollected = 1,

    /// <summary>収集が途絶えている。</summary>
    Stale = 2,
}

public record TargetMonitoringHealth
{
    public required long TargetId { get; init; }

    public required string TargetName { get; init; }

    public required MonitoringReach Reach { get; init; }

    public DateTime? LastCollectedAt { get; init; }

    /// <summary>この対象に期待される収集間隔(秒)。</summary>
    public required int ExpectedIntervalSeconds { get; init; }

    /// <summary>最後の収集から経過した秒数。一度も無ければnull。</summary>
    public long? StaleForSeconds { get; init; }

    public bool IsHealthy => Reach == MonitoringReach.Reaching;
}

public interface IMonitoringHealthService
{
    /// <summary>有効な監視対象について、収集が届いているかを判定する。</summary>
    Task<List<TargetMonitoringHealth>> GetAsync(CancellationToken ct = default);
}

/// <summary>
/// 自己監視。収集が止まっていることを検知する。
///
/// これが無いと「障害が無い」と「監視が死んでいる」が画面で区別できない。
/// どちらもインシデント0件として見えるため、監視が止まったことに誰も気づけない。
///
/// 判定はAPI側で、読むたびに計算する。Worker側のジョブとして作ると、
/// Workerが死んだときに「Workerが死んだ」と報せる担当も一緒に死ぬ。
/// 収集の記録(MetricSnapshot)そのものを生存の証跡として使うため、
/// 別に心拍を書く仕組みは持たない。書く側が死ねば心拍も止まるので同じこと。
/// </summary>
public class MonitoringHealthService(
    IMonitoringTargetRepository targets,
    IMetricSnapshotRepository snapshots,
    TimeProvider timeProvider) : IMonitoringHealthService
{
    /// <summary>
    /// 収集間隔の何倍まで待つか。
    /// 1回の取りこぼしや実行の揺れで「止まった」と言わないための余裕。
    /// </summary>
    public const int StaleAfterIntervals = 3;

    /// <summary>
    /// 上乗せする猶予。間隔が短い対象で、わずかな遅れが即異常にならないようにする。
    /// </summary>
    public static readonly TimeSpan Grace = TimeSpan.FromMinutes(1);

    public async Task<List<TargetMonitoringHealth>> GetAsync(CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var all = await targets.GetAllAsync(ct);
        var latest = await snapshots.GetLatestCollectedAtByTargetAsync(ct);

        var result = new List<TargetMonitoringHealth>();

        foreach (var target in all)
        {
            // 監視していない対象は、収集が無くて当たり前なので対象外
            if (!target.IsEnabled)
            {
                continue;
            }

            var intervalSeconds = target.CollectionIntervalSeconds ?? DefaultIntervalSeconds;
            var threshold = TimeSpan.FromSeconds(intervalSeconds * StaleAfterIntervals) + Grace;

            if (!latest.TryGetValue(target.Id, out var lastCollectedAt))
            {
                // 登録直後はまだ収集が回っていないため、猶予のうちは異常としない
                var sinceCreated = now - target.CreatedAt;
                result.Add(new TargetMonitoringHealth
                {
                    TargetId = target.Id,
                    TargetName = target.Name,
                    Reach = sinceCreated > threshold
                        ? MonitoringReach.NeverCollected
                        : MonitoringReach.Reaching,
                    LastCollectedAt = null,
                    ExpectedIntervalSeconds = intervalSeconds,
                    StaleForSeconds = null,
                });
                continue;
            }

            var elapsed = now - lastCollectedAt;
            result.Add(new TargetMonitoringHealth
            {
                TargetId = target.Id,
                TargetName = target.Name,
                Reach = elapsed > threshold ? MonitoringReach.Stale : MonitoringReach.Reaching,
                LastCollectedAt = lastCollectedAt,
                ExpectedIntervalSeconds = intervalSeconds,
                // 時計のずれで負になった場合は0として扱う
                StaleForSeconds = (long)Math.Max(0, elapsed.TotalSeconds),
            });
        }

        return result;
    }

    /// <summary>
    /// 対象に指定が無い場合の間隔。全体の既定(毎分)に合わせる。
    /// 設定値そのものはWorker側にあるため、ここでは検知が緩くなる側へ倒す。
    /// </summary>
    private const int DefaultIntervalSeconds = 60;
}
