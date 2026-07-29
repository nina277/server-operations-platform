namespace ServerOperations.Core.Models.Operations;

public enum CollectionStatus
{
    Ok = 0,

    /// <summary>収集失敗。古い値を正常値として扱わず、失敗として記録する。</summary>
    Failed = 1,
}

/// <summary>対象別の収集スナップショット。正規化済みメトリクスをJSONで保持する。</summary>
public class MetricSnapshot
{
    public long Id { get; set; }

    public long TargetId { get; set; }

    /// <summary>収集時刻(UTC)。</summary>
    public DateTime CollectedAt { get; set; }

    /// <summary>収集種別(docker / http)。</summary>
    public required string Kind { get; set; }

    public CollectionStatus Status { get; set; }

    /// <summary>正規化済みメトリクス(JSON)。収集失敗時はnull。</summary>
    public string? PayloadJson { get; set; }

    /// <summary>収集失敗時の理由(秘密情報を含めない)。</summary>
    public string? ErrorMessage { get; set; }
}
