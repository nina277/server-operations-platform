namespace ServerOperations.Core.Models.Operations;

public enum NotificationSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

public enum NotificationChannel
{
    /// <summary>画面内通知。</summary>
    InApp = 0,

    Email = 1,

    /// <summary>Web Push (FCM)。</summary>
    Push = 2,
}

public enum NotificationDeliveryStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2,

    /// <summary>チャネルが未設定・無効のため送信しなかった。</summary>
    Skipped = 3,
}

/// <summary>
/// 通知。同一障害署名は集約し、再通知間隔を超えるまで新規通知を作らない。
/// 本文に秘密情報やログ全文を含めない。
/// </summary>
public class Notification
{
    public long Id { get; set; }

    public NotificationSeverity Severity { get; set; }

    public required string Title { get; set; }

    /// <summary>本文。ログ全文・秘密情報を含めないこと。</summary>
    public required string Body { get; set; }

    /// <summary>集約キー(障害署名など)。同一キーの通知は集約する。</summary>
    public string? AggregationKey { get; set; }

    public long? IncidentId { get; set; }

    public long? TargetId { get; set; }

    /// <summary>集約された発生回数。</summary>
    public int OccurrenceCount { get; set; } = 1;

    public DateTime FirstNotifiedAt { get; set; }

    public DateTime LastNotifiedAt { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public ICollection<NotificationDelivery> Deliveries { get; set; } = [];
}

/// <summary>チャネル別の配信結果。</summary>
public class NotificationDelivery
{
    public long Id { get; set; }

    public long NotificationId { get; set; }

    public Notification? Notification { get; set; }

    public NotificationChannel Channel { get; set; }

    public NotificationDeliveryStatus Status { get; set; }

    /// <summary>失敗理由の要約(秘密情報を含めない)。</summary>
    public string? ErrorMessage { get; set; }

    public DateTime AttemptedAt { get; set; }
}

/// <summary>
/// Web Push配信先の端末トークン。送信に失敗した端末のトークンは失効させる。
/// </summary>
public class DeviceToken
{
    public long Id { get; set; }

    public long UserId { get; set; }

    /// <summary>FCM登録トークン。</summary>
    public required string Token { get; set; }

    /// <summary>端末の識別用ラベル(利用者が付ける名前)。</summary>
    public string? Label { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    /// <summary>失効日時。失効済みトークンへは送信しない。</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>連続失敗回数。閾値に達したら自動失効させる。</summary>
    public int ConsecutiveFailureCount { get; set; }
}
