using System.ComponentModel.DataAnnotations;

namespace ServerOperations.Api.DTOs.Operations;

/// <summary>所要時間の分布。平均だけでは外れ値に引きずられるため中央値と最大も返す。</summary>
public record DurationStatsDto
{
    public required int Count { get; init; }

    public double? AverageSeconds { get; init; }

    public double? MedianSeconds { get; init; }

    /// <summary>95パーセンタイル。件数が少ないときは最大値に近づく。</summary>
    public double? P95Seconds { get; init; }

    public double? MaxSeconds { get; init; }
}

/// <summary>
/// 運用実績サマリ。docs/verification.md の成功基準を画面から測れるようにする。
/// </summary>
public record OperationsInsightsDto
{
    public required DateTime From { get; init; }

    public required DateTime To { get; init; }

    /// <summary>成功基準#2: 検知から通知までの所要時間。</summary>
    public required DurationStatsDto DetectionToNotification { get; init; }

    /// <summary>基準の秒数(既定300秒)以内に通知できた割合。件数0ならnull。</summary>
    public double? NotifiedWithinTargetRatio { get; init; }

    public required int NotificationTargetSeconds { get; init; }

    /// <summary>復旧の所要時間(成功したもの)。</summary>
    public required DurationStatsDto RecoveryDuration { get; init; }

    public required DurationStatsDto AutoRecoveryDuration { get; init; }

    public required int IncidentsDetected { get; init; }

    public required int IncidentsResolved { get; init; }

    public required Dictionary<string, int> IncidentsBySeverity { get; init; }

    public required Dictionary<string, int> RecoveryByStatus { get; init; }

    public required Dictionary<string, int> AutoRecoveryByStatus { get; init; }

    /// <summary>自動実行の成功率。実行した件数が0ならnull。</summary>
    public double? AutoRecoverySuccessRatio { get; init; }

    /// <summary>
    /// 安全機構が自動実行を止めた理由の内訳。
    /// 何回・何を止めたかは、この仕組みが効いていることの直接の証拠になる。
    /// </summary>
    public required Dictionary<string, int> BlockedReasons { get; init; }
}

/// <summary>同一障害の再発状況。</summary>
public record RecurrenceDto
{
    public required int TotalCount { get; init; }

    public required int ResolvedCount { get; init; }

    public DateTime? FirstOccurredAt { get; init; }

    public DateTime? PreviousOccurredAt { get; init; }

    /// <summary>前回この障害を解決した復旧操作のID。</summary>
    public string? LastSuccessfulActionId { get; init; }

    public DateTime? LastSuccessfulAt { get; init; }
}

public record IncidentNoteDto
{
    public required long Id { get; init; }

    public required string AuthorName { get; init; }

    public required string Body { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public record CreateIncidentNoteRequest
{
    [Required(ErrorMessage = "メモの内容を入力してください。")]
    [MaxLength(4000, ErrorMessage = "メモは4000文字以内で入力してください。")]
    public required string Body { get; init; }
}

public record MaintenanceWindowDto
{
    public required long Id { get; init; }

    /// <summary>nullならすべての監視対象が対象。</summary>
    public long? TargetId { get; init; }

    public string? TargetName { get; init; }

    public required string Reason { get; init; }

    public required DateTime StartsAt { get; init; }

    public required DateTime EndsAt { get; init; }

    public required bool SuppressNotifications { get; init; }

    public required bool SuppressAutoRecovery { get; init; }

    public DateTime? CancelledAt { get; init; }

    /// <summary>いま効いているか。</summary>
    public required bool IsActive { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public record CreateMaintenanceWindowRequest
{
    /// <summary>省略するとすべての監視対象が対象になる。</summary>
    public long? TargetId { get; init; }

    [Required(ErrorMessage = "メンテナンスの理由を入力してください。")]
    [MaxLength(200, ErrorMessage = "理由は200文字以内で入力してください。")]
    public required string Reason { get; init; }

    [Required]
    public required DateTime StartsAt { get; init; }

    [Required]
    public required DateTime EndsAt { get; init; }

    public bool SuppressNotifications { get; init; } = true;

    public bool SuppressAutoRecovery { get; init; } = true;
}

public record NotificationTestResultDto
{
    public required string Channel { get; init; }

    public required bool Success { get; init; }

    /// <summary>設定が無く送らなかった場合はtrue。失敗とは区別する。</summary>
    public required bool Skipped { get; init; }

    public string? Message { get; init; }
}
