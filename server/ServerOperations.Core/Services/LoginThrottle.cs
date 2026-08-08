using System.Collections.Concurrent;

namespace ServerOperations.Core.Services;

public record LoginThrottleDecision(bool Allowed, TimeSpan RetryAfter)
{
    public static readonly LoginThrottleDecision Allow = new(true, TimeSpan.Zero);
}

public interface ILoginThrottle
{
    /// <summary>いま試行を受け付けてよいかを判定する。</summary>
    LoginThrottleDecision Check(string username, string? ipAddress);

    void RecordFailure(string username, string? ipAddress);

    /// <summary>成功したら、その利用者の記録を消す。</summary>
    void RecordSuccess(string username, string? ipAddress);
}

/// <summary>
/// ログイン試行の制限。総当たりを実用的でない速さまで落とす。
///
/// 復旧操作にはクールダウン・回数上限・サーキットブレーカーを積んでいるのに、
/// その入口である認証が素通しでは釣り合わない。
///
/// 利用者名とIPアドレスの両方で数える。
/// 利用者名だけだと、多数の利用者名へ1回ずつ試す攻撃(スプレー)を止められない。
/// IPだけだと、共有回線の裏にいる利用者を巻き込みやすい。
///
/// 記録はプロセス内に持つ。試行のたびにDBへ書くと、
/// 失敗を投げ込むだけでDBを膨らませられる経路を自分で作ることになるため。
/// 単一ノードで動かす前提の設計で、再起動すると数え直しになる。
/// 恒久的な記録は監査ログ側に残る。
/// </summary>
public class LoginThrottle(TimeProvider timeProvider) : ILoginThrottle
{
    /// <summary>この時間だけ失敗を数える。最後の失敗から数え直す。</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    /// <summary>同じ利用者名がこの回数を超えて失敗したら止める。</summary>
    public const int MaxFailuresPerUser = 5;

    /// <summary>同じIPがこの回数を超えて失敗したら止める(スプレー対策)。</summary>
    public const int MaxFailuresPerIp = 20;

    private static readonly TimeSpan BaseLockout = TimeSpan.FromMinutes(1);

    private static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 保持する鍵の上限。存在しない利用者名を無数に投げ込まれても
    /// メモリが際限なく増えないようにする。
    /// </summary>
    private const int MaxTrackedKeys = 10_000;

    private readonly ConcurrentDictionary<string, Attempt> _attempts = new();

    private sealed class Attempt
    {
        public int Failures;

        public DateTimeOffset LastFailureAt;
    }

    public LoginThrottleDecision Check(string username, string? ipAddress)
    {
        var now = timeProvider.GetUtcNow();

        // 利用者名とIPのうち、より長く待たせる方を採用する
        var byUser = Evaluate(UserKey(username), MaxFailuresPerUser, now);
        var byIp = ipAddress is null
            ? LoginThrottleDecision.Allow
            : Evaluate(IpKey(ipAddress), MaxFailuresPerIp, now);

        if (byUser.Allowed && byIp.Allowed)
        {
            return LoginThrottleDecision.Allow;
        }

        return byUser.RetryAfter >= byIp.RetryAfter ? byUser : byIp;
    }

    public void RecordFailure(string username, string? ipAddress)
    {
        var now = timeProvider.GetUtcNow();

        Prune(now);
        Increment(UserKey(username), now);

        if (ipAddress is not null)
        {
            Increment(IpKey(ipAddress), now);
        }
    }

    public void RecordSuccess(string username, string? ipAddress)
    {
        // 利用者名の記録だけを消す。IPの記録は残す。
        // 攻撃者が自分のアカウントで成功できると、IP側の制限を毎回消せてしまう。
        _attempts.TryRemove(UserKey(username), out _);
        _ = ipAddress;
    }

    private LoginThrottleDecision Evaluate(string key, int threshold, DateTimeOffset now)
    {
        if (!_attempts.TryGetValue(key, out var attempt))
        {
            return LoginThrottleDecision.Allow;
        }

        // 最後の失敗から一定時間が空いたら数え直す
        if (now - attempt.LastFailureAt >= Window)
        {
            _attempts.TryRemove(key, out _);
            return LoginThrottleDecision.Allow;
        }

        if (attempt.Failures <= threshold)
        {
            return LoginThrottleDecision.Allow;
        }

        var lockout = LockoutFor(attempt.Failures - threshold);
        var unlockAt = attempt.LastFailureAt + lockout;

        return now >= unlockAt
            ? LoginThrottleDecision.Allow
            : new LoginThrottleDecision(false, unlockAt - now);
    }

    /// <summary>超過分だけ待ち時間を倍にする。上限で頭打ちにする。</summary>
    private static TimeSpan LockoutFor(int excessFailures)
    {
        // 桁あふれを避けるため、倍にする回数そのものを先に抑える
        var doublings = Math.Min(excessFailures - 1, 16);
        var seconds = BaseLockout.TotalSeconds * Math.Pow(2, doublings);
        return seconds >= MaxLockout.TotalSeconds ? MaxLockout : TimeSpan.FromSeconds(seconds);
    }

    private void Increment(string key, DateTimeOffset now)
    {
        _attempts.AddOrUpdate(
            key,
            _ => new Attempt { Failures = 1, LastFailureAt = now },
            (_, existing) =>
            {
                // 期限切れなら数え直す
                if (now - existing.LastFailureAt >= Window)
                {
                    existing.Failures = 1;
                }
                else
                {
                    existing.Failures++;
                }

                existing.LastFailureAt = now;
                return existing;
            });
    }

    /// <summary>期限切れの記録を落とす。上限を超えていたら古い順に切り詰める。</summary>
    private void Prune(DateTimeOffset now)
    {
        foreach (var (key, attempt) in _attempts)
        {
            if (now - attempt.LastFailureAt >= Window)
            {
                _attempts.TryRemove(key, out _);
            }
        }

        if (_attempts.Count <= MaxTrackedKeys)
        {
            return;
        }

        foreach (var (key, _) in _attempts
            .OrderBy(pair => pair.Value.LastFailureAt)
            .Take(_attempts.Count - MaxTrackedKeys))
        {
            _attempts.TryRemove(key, out _);
        }
    }

    // 利用者名とIPが同じ文字列でも別々に数えるよう、接頭辞で分ける。
    // 利用者名は大小を区別せずに数える(Admin と admin を別枠にしない)。
    private static string UserKey(string username) => "u:" + username.Trim().ToLowerInvariant();

    private static string IpKey(string ipAddress) => "i:" + ipAddress;
}
