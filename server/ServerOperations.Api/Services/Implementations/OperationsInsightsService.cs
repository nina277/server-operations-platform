using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>
/// 運用実績の集計。docs/verification.md の成功基準を、SQLを手で叩かずに測れるようにする。
///
/// 集計対象はすでに保存済みのインシデント・通知・復旧操作のみ。
/// この口は読み取りに限り、何も書き換えない。
/// </summary>
public class OperationsInsightsService(
    IOperationsInsightsRepository insights) : IOperationsInsightsService
{
    /// <summary>成功基準#2 の「検知から通知まで5分以内」に対応する。</summary>
    private const int NotificationTargetSeconds = 300;

    /// <summary>期間の上限。長くしすぎると1回の問い合わせが重くなる。</summary>
    public const int MaxRangeDays = 366;

    public async Task<OperationsInsightsDto> GetAsync(
        DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (from >= to)
        {
            throw AppException.BadRequest("invalid_range", "開始日時は終了日時より前にしてください。");
        }

        if ((to - from).TotalDays > MaxRangeDays)
        {
            throw AppException.BadRequest(
                "range_too_wide", $"集計できる期間は最長{MaxRangeDays}日です。");
        }

        var range = new OperationsInsightsRange(from, to);

        var detection = await insights.GetDetectionToNotificationSecondsAsync(range, ct);
        var recovery = await insights.GetRecoveryDurationSecondsAsync(range, automaticOnly: false, ct);
        var autoRecovery = await insights.GetRecoveryDurationSecondsAsync(range, automaticOnly: true, ct);
        var byStatus = await insights.CountRecoveryByStatusAsync(range, automaticOnly: false, ct);
        var autoByStatus = await insights.CountRecoveryByStatusAsync(range, automaticOnly: true, ct);
        var blocked = await insights.CountBlockedReasonsAsync(range, ct);
        var bySeverity = await insights.CountIncidentsBySeverityAsync(range, ct);
        var (detected, resolved) = await insights.CountIncidentOutcomesAsync(range, ct);

        return new OperationsInsightsDto
        {
            From = from,
            To = to,
            DetectionToNotification = Summarize(detection),
            NotifiedWithinTargetRatio = Ratio(
                detection.Count(s => s <= NotificationTargetSeconds), detection.Count),
            NotificationTargetSeconds = NotificationTargetSeconds,
            RecoveryDuration = Summarize(recovery),
            AutoRecoveryDuration = Summarize(autoRecovery),
            IncidentsDetected = detected,
            IncidentsResolved = resolved,
            IncidentsBySeverity = bySeverity.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            RecoveryByStatus = byStatus.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            AutoRecoveryByStatus = autoByStatus.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            AutoRecoverySuccessRatio = SuccessRatio(autoByStatus),
            BlockedReasons = blocked,
        };
    }

    /// <summary>
    /// 成功率は「実行を試みた件数」に対して測る。
    /// 安全機構が止めた分(Blocked)を分母に入れると、止めるほど成功率が下がり、
    /// 安全に止めていることが悪い結果として出てしまうため除く。
    /// </summary>
    private static double? SuccessRatio(Dictionary<RecoveryActionStatus, int> byStatus)
    {
        var succeeded = byStatus.GetValueOrDefault(RecoveryActionStatus.Succeeded);
        var failed = byStatus.GetValueOrDefault(RecoveryActionStatus.Failed);
        return Ratio(succeeded, succeeded + failed);
    }

    private static double? Ratio(int numerator, int denominator) =>
        denominator == 0 ? null : (double)numerator / denominator;

    private static DurationStatsDto Summarize(List<double> values)
    {
        if (values.Count == 0)
        {
            return new DurationStatsDto { Count = 0 };
        }

        var sorted = values.OrderBy(v => v).ToList();

        return new DurationStatsDto
        {
            Count = sorted.Count,
            AverageSeconds = Math.Round(sorted.Average(), 1),
            MedianSeconds = Math.Round(Percentile(sorted, 0.50), 1),
            P95Seconds = Math.Round(Percentile(sorted, 0.95), 1),
            MaxSeconds = Math.Round(sorted[^1], 1),
        };
    }

    /// <summary>
    /// 線形補間による分位点。件数が少ないときに階段状の値にならないようにする。
    /// 呼び出し側で昇順に並べ替え済みであること。
    /// </summary>
    private static double Percentile(List<double> sorted, double fraction)
    {
        if (sorted.Count == 1)
        {
            return sorted[0];
        }

        var position = fraction * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var upper = (int)Math.Ceiling(position);

        return lower == upper
            ? sorted[lower]
            : sorted[lower] + ((sorted[upper] - sorted[lower]) * (position - lower));
    }
}
