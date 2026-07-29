using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services.Ai;

/// <summary>AIへ渡す診断入力。呼び出し前に匿名化・縮小済みであること。</summary>
public record AiDiagnosisInput
{
    public required string Classification { get; init; }

    public string? Service { get; init; }

    /// <summary>匿名化・縮小済みのログ抜粋。</summary>
    public required string LogExcerpt { get; init; }

    /// <summary>この対象で実際に実行できるアクションID。AIはここから選ぶ。</summary>
    public required IReadOnlyList<string> AllowedActionIds { get; init; }
}

/// <summary>
/// AIの診断結果。プロバイダーが返した生の値であり、この時点では信用しない。
/// Service層で対象能力・許可リスト・危険度・承認条件を再検証する。
/// </summary>
public record AiDiagnosisOutput
{
    public required string Classification { get; init; }

    public required IncidentSeverity Severity { get; init; }

    public required string Rationale { get; init; }

    /// <summary>AIが推奨したアクションID。そのまま実行しない。</summary>
    public string? RecommendedActionId { get; init; }

    /// <summary>AIが示した確信度(0.0〜1.0)。</summary>
    public double? Confidence { get; init; }
}

public enum AiCallOutcome
{
    Success = 0,
    InvalidResponse = 1,
    RateLimited = 2,
    Timeout = 3,
    Error = 4,
}

public record AiCallResponse(
    AiCallOutcome Outcome,
    AiDiagnosisOutput? Output = null,
    string? ErrorSummary = null,
    int? OutputTokens = null,
    long? LatencyMs = null);

public interface IAiDiagnosisProvider
{
    string ProviderName { get; }

    /// <summary>
    /// AIへ診断を依頼する。例外を投げず、失敗はAiCallOutcomeで返す。
    /// </summary>
    Task<AiCallResponse> DiagnoseAsync(
        AiDiagnosisInput input, AiUsageLimit limits, string apiKey, CancellationToken ct = default);
}
