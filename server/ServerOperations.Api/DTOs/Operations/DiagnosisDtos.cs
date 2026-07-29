using System.ComponentModel.DataAnnotations;
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

/// <summary>診断ルールの作成・更新の入力。条件は種別ごとの構造で受け取る。</summary>
public record SaveDiagnosticRuleRequest
{
    [Required]
    [MaxLength(100)]
    public required string Name { get; init; }

    [Required]
    [MaxLength(64)]
    public required string Classification { get; init; }

    /// <summary>State / Threshold / Regex。</summary>
    [Required]
    [MaxLength(16)]
    public required string RuleType { get; init; }

    /// <summary>条件(JSON)。保存前に種別ごとに検証する。</summary>
    [Required]
    [MaxLength(2000)]
    public required string ConditionJson { get; init; }

    /// <summary>Critical / High / Medium / Low。</summary>
    [Required]
    [MaxLength(16)]
    public required string Severity { get; init; }

    /// <summary>推奨アクションID。許可リストにあるIDのみ。nullなら通知のみ。</summary>
    [MaxLength(64)]
    public string? RecommendedActionId { get; init; }

    [Range(1, 1000)]
    public int Priority { get; init; } = 100;

    /// <summary>根拠テンプレート。{field} {value} {expected} を実値で置き換える。</summary>
    [Required]
    [MaxLength(500)]
    public required string RationaleTemplate { get; init; }

    public bool IsEnabled { get; init; } = true;
}

public record UpdateRuleEnabledRequest
{
    public required bool IsEnabled { get; init; }
}

/// <summary>ルールを書くときに選べる値。画面の入力欄を組み立てるために返す。</summary>
public record RuleEditorOptionsDto
{
    /// <summary>条件で参照できる項目。 </summary>
    public required IReadOnlyList<string> Fields { get; init; }

    public required IReadOnlyList<string> Operators { get; init; }

    public required IReadOnlyList<string> RuleTypes { get; init; }

    public required IReadOnlyList<string> Severities { get; init; }

    /// <summary>推奨アクションに指定できるID(復旧の許可リスト)。</summary>
    public required IReadOnlyList<string> RecommendedActionIds { get; init; }
}
