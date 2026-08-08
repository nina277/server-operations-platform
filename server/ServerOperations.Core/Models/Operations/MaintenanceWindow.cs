namespace ServerOperations.Core.Models.Operations;

/// <summary>
/// メンテナンス期間。計画停止中は通知と自動復旧を止める。
///
/// 検知そのものは止めない。期間中に起きたことは記録として残し、
/// 「知らせない」「勝手に直さない」だけを制御する。
/// 記録まで止めると、計画停止をまたいで続いた障害を後から追えなくなる。
/// </summary>
public class MaintenanceWindow
{
    public long Id { get; set; }

    /// <summary>対象。nullならすべての監視対象に効く。</summary>
    public long? TargetId { get; set; }

    public MonitoringTarget? Target { get; set; }

    /// <summary>何のための停止かを残す(後から見て理由が分かるようにする)。</summary>
    public required string Reason { get; set; }

    public DateTime StartsAt { get; set; }

    public DateTime EndsAt { get; set; }

    /// <summary>期間中に通知を止めるか。</summary>
    public bool SuppressNotifications { get; set; } = true;

    /// <summary>期間中に自動復旧を止めるか。</summary>
    public bool SuppressAutoRecovery { get; set; } = true;

    /// <summary>取り消した場合の時刻。期間前でも途中でも取り消せる。</summary>
    public DateTime? CancelledAt { get; set; }

    public long? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>指定時刻にこの期間が効いているか。</summary>
    public bool IsActiveAt(DateTime instant) =>
        CancelledAt is null && StartsAt <= instant && instant < EndsAt;
}
