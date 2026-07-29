namespace ServerOperations.Core.Models.Operations;

public enum IncidentStatus
{
    Open = 0,
    Acknowledged = 1,
    Recovering = 2,
    Resolved = 3,
    Closed = 4,
}

public enum IncidentSeverity
{
    Low = 0,
    Medium = 1,
    High = 2,
    Critical = 3,
}

/// <summary>
/// インシデント。対象・サービス・分類・正規化ログから算出したSHA-256障害署名で重複を集約する。
/// 同一署名の再発は新規レコードを作らず、既存(未Close)レコードへ関連付ける。
/// </summary>
public class Incident
{
    public long Id { get; set; }

    public long TargetId { get; set; }

    /// <summary>障害署名(SHA-256、Base64)。</summary>
    public required string SignatureSha256 { get; set; }

    public required string Title { get; set; }

    /// <summary>障害分類(例: ContainerStopped, HttpUnavailable, CollectionFailed)。</summary>
    public required string Classification { get; set; }

    /// <summary>影響サービス(コンテナ名等)。</summary>
    public string? Service { get; set; }

    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Medium;

    public IncidentStatus Status { get; set; } = IncidentStatus.Open;

    public DateTime FirstOccurredAt { get; set; }

    public DateTime LastOccurredAt { get; set; }

    /// <summary>同一署名の発生回数。</summary>
    public int OccurrenceCount { get; set; } = 1;

    public DateTime? ResolvedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
