namespace ServerOperations.Core.Models.Operations;

public enum HealthCheckResultStatus
{
    Healthy = 0,
    Unhealthy = 1,

    /// <summary>チェック自体を実行できなかった。</summary>
    Error = 2,
}

/// <summary>
/// ヘルスチェック結果。復旧実行後に別ジョブで実行し、復旧の成否を確認する。
/// </summary>
public class HealthCheck
{
    public long Id { get; set; }

    public long TargetId { get; set; }

    /// <summary>復旧後チェックの場合、対象の復旧アクション。</summary>
    public long? RecoveryActionId { get; set; }

    public HealthCheckResultStatus Status { get; set; }

    /// <summary>結果の要約(秘密情報を含めない)。</summary>
    public required string Message { get; set; }

    public long? LatencyMs { get; set; }

    public DateTime CheckedAt { get; set; }
}
