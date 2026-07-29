using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Api.DTOs.Settings;

/// <summary>
/// 監査ログの表示用。
/// Detailsは記録時点で秘密情報を含めない運用のため、そのまま返す。
/// </summary>
public record AuditLogDto
{
    public required long Id { get; init; }

    public required DateTime OccurredAt { get; init; }

    public long? ActorUserId { get; init; }

    public string? ActorName { get; init; }

    public required string IpAddress { get; init; }

    public required string UserAgent { get; init; }

    public required string TargetType { get; init; }

    public string? TargetId { get; init; }

    public required string Action { get; init; }

    public required string Result { get; init; }

    public string? Details { get; init; }

    public string? TraceId { get; init; }

    public static AuditLogDto From(AuditLog a) => new()
    {
        Id = a.Id,
        OccurredAt = a.OccurredAt,
        ActorUserId = a.ActorUserId,
        ActorName = a.ActorName,
        IpAddress = a.IpAddress,
        UserAgent = a.UserAgent,
        TargetType = a.TargetType,
        TargetId = a.TargetId,
        Action = a.Action,
        Result = a.Result.ToString(),
        Details = a.Details,
        TraceId = a.TraceId,
    };
}

/// <summary>監査ログの絞り込みに使える選択肢。</summary>
public record AuditLogFilterOptionsDto
{
    public required IReadOnlyList<string> TargetTypes { get; init; }

    public required IReadOnlyList<string> Actions { get; init; }

    public required IReadOnlyList<string> Results { get; init; }
}
