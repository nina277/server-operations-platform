using System.ComponentModel.DataAnnotations;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Api.DTOs.Operations;

public record IncidentDto
{
    public required long Id { get; init; }

    public required long TargetId { get; init; }

    public required string Title { get; init; }

    public required string Classification { get; init; }

    public string? Service { get; init; }

    public required string Severity { get; init; }

    public required string Status { get; init; }

    public required DateTime FirstOccurredAt { get; init; }

    public required DateTime LastOccurredAt { get; init; }

    public required int OccurrenceCount { get; init; }

    public DateTime? ResolvedAt { get; init; }

    public static IncidentDto From(Incident incident) => new()
    {
        Id = incident.Id,
        TargetId = incident.TargetId,
        Title = incident.Title,
        Classification = incident.Classification,
        Service = incident.Service,
        Severity = incident.Severity.ToString(),
        Status = incident.Status.ToString(),
        FirstOccurredAt = incident.FirstOccurredAt,
        LastOccurredAt = incident.LastOccurredAt,
        OccurrenceCount = incident.OccurrenceCount,
        ResolvedAt = incident.ResolvedAt,
    };
}

public record IncidentListQuery
{
    public string? Status { get; init; }

    public string? Severity { get; init; }

    public long? TargetId { get; init; }

    [MaxLength(100)]
    public string? Search { get; init; }

    /// <summary>並び替え(lastOccurredAt / firstOccurredAt / severity / status。- 接頭辞で降順)。</summary>
    [MaxLength(32)]
    public string Sort { get; init; } = "-lastOccurredAt";

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public record UpdateIncidentStatusRequest
{
    /// <summary>Open / Acknowledged / Recovering / Resolved / Closed。</summary>
    [Required]
    [MaxLength(16)]
    public required string Status { get; init; }
}

public record DashboardSummaryDto
{
    public required int TargetCount { get; init; }

    public required int EnabledTargetCount { get; init; }

    public required Dictionary<string, int> ActiveIncidentsBySeverity { get; init; }

    public required Dictionary<string, int> IncidentsByStatus { get; init; }

    public required List<IncidentDto> RecentIncidents { get; init; }
}

public record MetricSnapshotDto
{
    public required long Id { get; init; }

    public required DateTime CollectedAt { get; init; }

    public required string Kind { get; init; }

    public required string Status { get; init; }

    public string? PayloadJson { get; init; }

    public string? ErrorMessage { get; init; }
}

public record IncidentLogDto
{
    public required long Id { get; init; }

    public required DateTime CollectedAt { get; init; }

    public required string Source { get; init; }

    public required string MaskedContent { get; init; }

    public long? IncidentId { get; init; }
}
