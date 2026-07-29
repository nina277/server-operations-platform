namespace ServerOperations.Core.Models.Operations;

public enum DiagnosisSource
{
    /// <summary>ルール評価による診断。</summary>
    Rule = 0,

    /// <summary>障害署名が一致する過去診断の再利用。</summary>
    History = 1,

    /// <summary>外部AIによる診断(T-08)。</summary>
    Ai = 2,
}

/// <summary>
/// 診断結果。診断元(ルール/履歴/AI)付きで保存する。
/// 推奨アクションは保存時点の対象能力・許可操作で再検証した結果を持つ。
/// </summary>
public class Diagnosis
{
    public long Id { get; set; }

    public long IncidentId { get; set; }

    public long TargetId { get; set; }

    public DiagnosisSource Source { get; set; }

    /// <summary>Source=Ruleのとき、一致したルールID。</summary>
    public long? RuleId { get; set; }

    /// <summary>Source=Historyのとき、再利用した過去診断ID。</summary>
    public long? ReusedDiagnosisId { get; set; }

    public required string Classification { get; set; }

    public IncidentSeverity Severity { get; set; }

    /// <summary>根拠(判定に使った条件と実値)。</summary>
    public required string Rationale { get; set; }

    public string? RecommendedActionId { get; set; }

    /// <summary>推奨アクションが現在の対象能力・許可操作で実行可能か(保存時点の再検証結果)。</summary>
    public bool RecommendedActionAllowed { get; set; }

    public DateTime CreatedAt { get; set; }
}
