namespace ServerOperations.Core.Models.Operations;

public enum ApprovalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}

/// <summary>
/// 復旧の承認。Medium操作は管理者承認とMFA再認証を必須とする。
/// </summary>
public class RecoveryApproval
{
    public long Id { get; set; }

    public long IncidentId { get; set; }

    public long TargetId { get; set; }

    /// <summary>承認対象のアクションID。</summary>
    public required string ActionId { get; set; }

    /// <summary>操作対象(コンテナ名等)。承認とアクションで一致することを検証する。</summary>
    public string? TargetResource { get; set; }

    public ApprovalStatus Status { get; set; } = ApprovalStatus.Pending;

    public long? DecidedByUserId { get; set; }

    public string? DecidedByUsername { get; set; }

    public DateTime? DecidedAt { get; set; }

    /// <summary>却下理由・承認コメント(秘密情報を含めない)。</summary>
    public string? Comment { get; set; }

    /// <summary>承認の有効期限。期限切れの承認では実行できない。</summary>
    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>この承認を使って実行済みか(1承認1実行)。</summary>
    public bool IsConsumed { get; set; }

    public bool IsUsable(DateTime nowUtc) =>
        Status == ApprovalStatus.Approved && !IsConsumed && nowUtc < ExpiresAt;
}
