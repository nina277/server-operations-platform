namespace ServerOperations.Core.Models.Auth;

public enum AuditResult
{
    Success = 0,
    Failure = 1,
    Denied = 2,
}

/// <summary>
/// 監査ログ。操作者、IPアドレス、User-Agent、対象、操作、結果、時刻を必ず保存する。
/// Detailsに秘密情報(パスワード、トークン等)を含めてはならない。
/// </summary>
public class AuditLog
{
    public long Id { get; set; }

    public DateTime OccurredAt { get; set; }

    public long? ActorUserId { get; set; }

    /// <summary>操作者名。未認証の試行ではリクエストで名乗られた識別子等を記録する。</summary>
    public string? ActorName { get; set; }

    public required string IpAddress { get; set; }

    public required string UserAgent { get; set; }

    /// <summary>対象の種別(例: User, RefreshToken, MonitoringTarget)。</summary>
    public required string TargetType { get; set; }

    public string? TargetId { get; set; }

    /// <summary>操作(例: auth.login, auth.refresh, auth.mfa.verify)。</summary>
    public required string Action { get; set; }

    public AuditResult Result { get; set; }

    /// <summary>補足情報。秘密情報を含めないこと。</summary>
    public string? Details { get; set; }

    public string? TraceId { get; set; }
}
