using System.ComponentModel.DataAnnotations;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Api.DTOs.Operations;

public record AiUsageSummaryDto
{
    public required bool IsEnabled { get; init; }

    public required string Provider { get; init; }

    public required string Model { get; init; }

    public required int HourlyUsed { get; init; }

    public required int HourlyLimit { get; init; }

    public required int DailyUsed { get; init; }

    public required int DailyLimit { get; init; }

    public required int MonthlyUsed { get; init; }

    public required int MonthlyLimit { get; init; }

    public required int MaxInputCharacters { get; init; }

    public required int MaxOutputTokens { get; init; }

    public required List<AiUsageRecordDto> RecentCalls { get; init; }
}

public record AiUsageRecordDto
{
    public required long Id { get; init; }

    public required DateTime CalledAt { get; init; }

    public required string Result { get; init; }

    public long? IncidentId { get; init; }

    public required int InputCharacters { get; init; }

    public int? OutputTokens { get; init; }

    public long? LatencyMs { get; init; }

    public string? ErrorSummary { get; init; }

    public static AiUsageRecordDto From(AiUsageRecord r) => new()
    {
        Id = r.Id,
        CalledAt = r.CalledAt,
        Result = r.Result.ToString(),
        IncidentId = r.IncidentId,
        InputCharacters = r.InputCharacters,
        OutputTokens = r.OutputTokens,
        LatencyMs = r.LatencyMs,
        ErrorSummary = r.ErrorSummary,
    };
}

public record UpdateAiLimitsRequest
{
    [MaxLength(64)]
    public string? Model { get; init; }

    [Range(1, 10000)]
    public int MonthlyLimit { get; init; } = 80;

    [Range(1, 1000)]
    public int DailyLimit { get; init; } = 10;

    [Range(1, 100)]
    public int HourlyLimit { get; init; } = 3;

    [Range(100, 100000)]
    public int MaxInputCharacters { get; init; } = 6000;

    [Range(50, 8000)]
    public int MaxOutputTokens { get; init; } = 500;

    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 20;
}

public record UpdateAiEnabledRequest
{
    public required bool IsEnabled { get; init; }
}

public record RediagnoseResultDto
{
    /// <summary>診断が得られた場合のみ設定される。</summary>
    public DiagnosisDto? Diagnosis { get; init; }

    /// <summary>診断できなかった場合の理由。</summary>
    public required string Outcome { get; init; }

    public string? Message { get; init; }
}
