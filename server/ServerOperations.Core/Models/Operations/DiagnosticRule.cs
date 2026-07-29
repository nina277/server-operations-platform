namespace ServerOperations.Core.Models.Operations;

public enum DiagnosticRuleType
{
    /// <summary>状態条件(例: containerState == exited)。</summary>
    State = 0,

    /// <summary>しきい値(例: memoryUsagePercent >= 90)。</summary>
    Threshold = 1,

    /// <summary>正規表現(ログ抜粋等へのパターン一致)。</summary>
    Regex = 2,
}

/// <summary>
/// 診断ルール。分類・危険度・根拠テンプレート・推奨アクションID・優先度を持つ。
/// 条件はルール種別ごとのJSONで保持する。
/// </summary>
public class DiagnosticRule
{
    public long Id { get; set; }

    public required string Name { get; set; }

    /// <summary>障害分類(ContainerStopped / HttpUnavailable / MemoryPressure / DiskPressure等)。</summary>
    public required string Classification { get; set; }

    public DiagnosticRuleType RuleType { get; set; }

    /// <summary>条件(JSON)。State: {field, equalsAny[]} / Threshold: {field, operator, value} / Regex: {field, pattern}。</summary>
    public required string ConditionJson { get; set; }

    public IncidentSeverity Severity { get; set; } = IncidentSeverity.Medium;

    /// <summary>推奨アクションID(許可リスト内のID。なしの場合はnull=通知のみ)。</summary>
    public string? RecommendedActionId { get; set; }

    /// <summary>評価順。小さいほど優先。</summary>
    public int Priority { get; set; } = 100;

    /// <summary>根拠テンプレート。{field} {value} {threshold} {pattern} を実値で置換する。</summary>
    public required string RationaleTemplate { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
