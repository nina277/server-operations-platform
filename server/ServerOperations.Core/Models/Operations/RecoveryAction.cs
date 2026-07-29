namespace ServerOperations.Core.Models.Operations;

/// <summary>復旧アクションの危険度。</summary>
public enum ActionRiskLevel
{
    Low = 0,
    Medium = 1,

    /// <summary>実行しない。影響説明と手順のみ提示する。</summary>
    High = 2,
}

public enum RecoveryActionStatus
{
    /// <summary>承認待ち(Medium操作)。</summary>
    PendingApproval = 0,

    Queued = 1,
    Running = 2,
    Succeeded = 3,
    Failed = 4,

    /// <summary>承認が却下された。</summary>
    Rejected = 5,

    /// <summary>クールダウン・回数上限・サーキットブレーカーにより実行されなかった。</summary>
    Blocked = 6,
}

/// <summary>
/// 復旧アクション。Workerだけが許可済みアクションを実行する。
/// 副作用を持つ要求はIdempotency-Keyで重複実行を防ぐ。
/// </summary>
public class RecoveryAction
{
    public long Id { get; set; }

    public long IncidentId { get; set; }

    public long TargetId { get; set; }

    /// <summary>アクションID(許可リスト内のID)。</summary>
    public required string ActionId { get; set; }

    /// <summary>操作対象(コンテナ名等)。</summary>
    public string? TargetResource { get; set; }

    public ActionRiskLevel RiskLevel { get; set; }

    public RecoveryActionStatus Status { get; set; } = RecoveryActionStatus.Queued;

    /// <summary>Medium操作で必要な承認。</summary>
    public long? ApprovalId { get; set; }

    /// <summary>副作用を持つ要求の冪等キー。同一キーの再送では新規実行しない。</summary>
    public string? IdempotencyKey { get; set; }

    public long? RequestedByUserId { get; set; }

    public DateTime RequestedAt { get; set; }

    public DateTime? StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>実行結果の要約(秘密情報を含めない)。</summary>
    public string? ResultMessage { get; set; }

    /// <summary>実行がブロックされた理由(cooldown / rate_limit / circuit_open 等)。</summary>
    public string? BlockedReason { get; set; }
}
