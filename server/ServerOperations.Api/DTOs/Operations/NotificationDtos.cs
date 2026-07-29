using System.ComponentModel.DataAnnotations;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Api.DTOs.Operations;

public record NotificationDto
{
    public required long Id { get; init; }

    public required string Severity { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public long? IncidentId { get; init; }

    public long? TargetId { get; init; }

    public required int OccurrenceCount { get; init; }

    public required DateTime FirstNotifiedAt { get; init; }

    public required DateTime LastNotifiedAt { get; init; }

    public required bool IsRead { get; init; }

    public static NotificationDto From(Notification n) => new()
    {
        Id = n.Id,
        Severity = n.Severity.ToString(),
        Title = n.Title,
        Body = n.Body,
        IncidentId = n.IncidentId,
        TargetId = n.TargetId,
        OccurrenceCount = n.OccurrenceCount,
        FirstNotifiedAt = n.FirstNotifiedAt,
        LastNotifiedAt = n.LastNotifiedAt,
        IsRead = n.IsRead,
    };
}

public record RegisterDeviceTokenRequest
{
    [Required]
    [MaxLength(512)]
    public required string Token { get; init; }

    [MaxLength(100)]
    public string? Label { get; init; }
}

public record DeviceTokenDto
{
    public required long Id { get; init; }

    public string? Label { get; init; }

    public required bool IsActive { get; init; }

    public required DateTime CreatedAt { get; init; }

    public DateTime? LastUsedAt { get; init; }

    public DateTime? RevokedAt { get; init; }

    /// <summary>トークン本体は返さず、識別用の末尾数文字のみ返す。</summary>
    public required string TokenSuffix { get; init; }

    public static DeviceTokenDto From(DeviceToken t) => new()
    {
        Id = t.Id,
        Label = t.Label,
        IsActive = t.IsActive,
        CreatedAt = t.CreatedAt,
        LastUsedAt = t.LastUsedAt,
        RevokedAt = t.RevokedAt,
        TokenSuffix = t.Token.Length <= 6 ? "***" : $"***{t.Token[^6..]}",
    };
}

public record RetentionPreviewDto
{
    public required int MetricSnapshots { get; init; }

    public required int IncidentLogs { get; init; }

    public required int Incidents { get; init; }

    public required int AuditLogs { get; init; }

    public required int Notifications { get; init; }

    public required int HealthChecks { get; init; }

    public required int Total { get; init; }

    public required long EstimatedBytes { get; init; }
}
