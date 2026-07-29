using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.DTOs.Operations;

public record DiagnosticRuleDto
{
    public required long Id { get; init; }

    public required string Name { get; init; }

    public required string Classification { get; init; }

    public required string RuleType { get; init; }

    public required string ConditionJson { get; init; }

    public required string Severity { get; init; }

    public string? RecommendedActionId { get; init; }

    public required int Priority { get; init; }

    public required bool IsEnabled { get; init; }

    public static DiagnosticRuleDto From(DiagnosticRule rule) => new()
    {
        Id = rule.Id,
        Name = rule.Name,
        Classification = rule.Classification,
        RuleType = rule.RuleType.ToString(),
        ConditionJson = rule.ConditionJson,
        Severity = rule.Severity.ToString(),
        RecommendedActionId = rule.RecommendedActionId,
        Priority = rule.Priority,
        IsEnabled = rule.IsEnabled,
    };
}

/// <summary>ルールのテスト入力。実際の収集値を模した任意の組み合わせを指定できる。</summary>
public record RuleTestRequest
{
    public string? ContainerState { get; init; }

    public string? ContainerName { get; init; }

    public int? RestartCount { get; init; }

    public double? MemoryUsagePercent { get; init; }

    public double? DiskUsagePercent { get; init; }

    public bool? HttpSuccess { get; init; }

    public int? HttpStatus { get; init; }

    public long? HttpLatencyMs { get; init; }

    public string? LogExcerpt { get; init; }

    public DiagnosticContext ToContext() => new()
    {
        ContainerState = ContainerState,
        ContainerName = ContainerName,
        RestartCount = RestartCount,
        MemoryUsagePercent = MemoryUsagePercent,
        DiskUsagePercent = DiskUsagePercent,
        HttpSuccess = HttpSuccess,
        HttpStatus = HttpStatus,
        HttpLatencyMs = HttpLatencyMs,
        LogExcerpt = LogExcerpt,
    };
}

public record RuleTestMatchDto
{
    public required long RuleId { get; init; }

    public required string RuleName { get; init; }

    public required string Classification { get; init; }

    public required string Severity { get; init; }

    public string? RecommendedActionId { get; init; }

    /// <summary>判定根拠(条件と実値)。</summary>
    public required string Rationale { get; init; }
}

public record RuleTestResponse
{
    public required IReadOnlyList<RuleTestMatchDto> Matches { get; init; }
}

public record DiagnosisDto
{
    public required long Id { get; init; }

    public required long IncidentId { get; init; }

    public required string Source { get; init; }

    public long? RuleId { get; init; }

    public long? ReusedDiagnosisId { get; init; }

    public required string Classification { get; init; }

    public required string Severity { get; init; }

    public required string Rationale { get; init; }

    public string? RecommendedActionId { get; init; }

    public required bool RecommendedActionAllowed { get; init; }

    public required DateTime CreatedAt { get; init; }

    public static DiagnosisDto From(Diagnosis diagnosis) => new()
    {
        Id = diagnosis.Id,
        IncidentId = diagnosis.IncidentId,
        Source = diagnosis.Source.ToString(),
        RuleId = diagnosis.RuleId,
        ReusedDiagnosisId = diagnosis.ReusedDiagnosisId,
        Classification = diagnosis.Classification,
        Severity = diagnosis.Severity.ToString(),
        Rationale = diagnosis.Rationale,
        RecommendedActionId = diagnosis.RecommendedActionId,
        RecommendedActionAllowed = diagnosis.RecommendedActionAllowed,
        CreatedAt = diagnosis.CreatedAt,
    };
}
