using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

/// <summary>レート制限の判定結果。ブロック時は理由コードを持つ。</summary>
public record RateLimitDecision(bool Allowed, string? Reason = null, string? Message = null)
{
    public static RateLimitDecision Allow() => new(true);

    public static RateLimitDecision Block(string reason, string message) => new(false, reason, message);
}

public class RecoveryLimits
{
    public const string SectionName = "Recovery";

    /// <summary>同一対象・同一アクションのクールダウン(分)。</summary>
    public int CooldownMinutes { get; set; } = 10;

    /// <summary>1時間あたりの最大実行回数。</summary>
    public int MaxPerHour { get; set; } = 2;

    /// <summary>サーキットブレーカー: 直近の連続失敗がこの回数に達したら遮断する。</summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 3;

    /// <summary>サーキットブレーカーの遮断時間(分)。</summary>
    public int CircuitBreakerOpenMinutes { get; set; } = 60;

    /// <summary>承認の有効期限(分)。</summary>
    public int ApprovalExpiryMinutes { get; set; } = 30;
}

public interface IRecoveryRateLimiter
{
    /// <summary>
    /// クールダウン・1時間あたりの回数上限・サーキットブレーカーを判定する。
    /// 副作用のないLow操作(RECHECK_HTTP_HEALTH)は対象外。
    /// </summary>
    Task<RateLimitDecision> CheckAsync(
        long targetId, string actionId, string? targetResource, CancellationToken ct = default);
}

public class RecoveryRateLimiter(
    IRecoveryActionRepository actions,
    RecoveryLimits limits,
    TimeProvider timeProvider) : IRecoveryRateLimiter
{
    public async Task<RateLimitDecision> CheckAsync(
        long targetId, string actionId, string? targetResource, CancellationToken ct = default)
    {
        // 副作用のない再チェックは制限しない
        if (actionId == RecoveryActionCatalog.RecheckHttpHealth)
        {
            return RateLimitDecision.Allow();
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 連続失敗の判定には十分な履歴が必要なため広めに取得する。
        // 個々の判定(クールダウン・時間あたり回数・遮断期限)は下で時刻を明示的に絞り込む。
        var recent = await actions.GetRecentForRateLimitAsync(
            targetId, actionId, targetResource, now.AddHours(-24), ct);

        // 実際に実行された(または実行中の)ものだけを対象にする。ブロック済み・却下は数えない。
        var executed = recent
            .Where(a => a.Status is RecoveryActionStatus.Queued
                or RecoveryActionStatus.Running
                or RecoveryActionStatus.Succeeded
                or RecoveryActionStatus.Failed)
            .OrderByDescending(a => a.RequestedAt)
            .ToList();

        // サーキットブレーカー: 直近の連続失敗が閾値以上なら遮断時間内は実行しない
        var consecutiveFailures = 0;
        foreach (var action in executed.Where(a =>
            a.Status is RecoveryActionStatus.Succeeded or RecoveryActionStatus.Failed))
        {
            if (action.Status == RecoveryActionStatus.Failed)
            {
                consecutiveFailures++;
            }
            else
            {
                break;
            }
        }

        if (consecutiveFailures >= limits.CircuitBreakerFailureThreshold)
        {
            var lastFailure = executed.First(a => a.Status == RecoveryActionStatus.Failed);
            var openUntil = (lastFailure.CompletedAt ?? lastFailure.RequestedAt)
                .AddMinutes(limits.CircuitBreakerOpenMinutes);
            if (now < openUntil)
            {
                return RateLimitDecision.Block(
                    "circuit_open",
                    $"復旧が{consecutiveFailures}回連続で失敗したため、{openUntil:yyyy-MM-dd HH:mm}(UTC)まで自動実行を停止しています。");
            }
        }

        // クールダウン
        var last = executed.FirstOrDefault();
        if (last is not null)
        {
            var cooldownUntil = last.RequestedAt.AddMinutes(limits.CooldownMinutes);
            if (now < cooldownUntil)
            {
                return RateLimitDecision.Block(
                    "cooldown",
                    $"前回の実行から{limits.CooldownMinutes}分以内です({cooldownUntil:yyyy-MM-dd HH:mm} UTC まで待機)。");
            }
        }

        // 1時間あたりの回数上限
        var withinHour = executed.Count(a => a.RequestedAt >= now.AddHours(-1));
        if (withinHour >= limits.MaxPerHour)
        {
            return RateLimitDecision.Block(
                "rate_limit",
                $"直近1時間の実行回数が上限({limits.MaxPerHour}回)に達しています。");
        }

        return RateLimitDecision.Allow();
    }
}
