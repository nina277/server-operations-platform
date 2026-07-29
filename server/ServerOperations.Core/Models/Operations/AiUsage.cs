namespace ServerOperations.Core.Models.Operations;

public enum AiCallResult
{
    Success = 0,

    /// <summary>応答が不正(JSON形式・スキーマ・Enum違反)。</summary>
    InvalidResponse = 1,

    /// <summary>レート制限(429)。</summary>
    RateLimited = 2,

    Timeout = 3,

    Error = 4,
}

/// <summary>
/// 外部AI呼び出しの記録。上限判定と利用状況の表示に使う。
/// プロンプト本文・応答本文は保存しない(秘密情報の残留を防ぐ)。
/// </summary>
public class AiUsageRecord
{
    public long Id { get; set; }

    public DateTime CalledAt { get; set; }

    public required string Provider { get; set; }

    public required string Model { get; set; }

    public long? IncidentId { get; set; }

    public AiCallResult Result { get; set; }

    /// <summary>送信した入力の文字数(本文は保存しない)。</summary>
    public int InputCharacters { get; set; }

    /// <summary>応答トークン数(プロバイダーが返した場合)。</summary>
    public int? OutputTokens { get; set; }

    public long? LatencyMs { get; set; }

    /// <summary>失敗理由の要約(応答本文は含めない)。</summary>
    public string? ErrorSummary { get; set; }
}

/// <summary>
/// 外部AIの利用上限。指示書6.3の初期値に従う。
/// </summary>
public class AiUsageLimit
{
    public long Id { get; set; }

    /// <summary>AI診断の有効/無効。初期状態はfalse。</summary>
    public bool IsEnabled { get; set; }

    public string Provider { get; set; } = "gemini";

    /// <summary>モデル名は設定値で指定する。</summary>
    public string Model { get; set; } = "gemini-2.0-flash";

    public int MonthlyLimit { get; set; } = 80;

    public int DailyLimit { get; set; } = 10;

    public int HourlyLimit { get; set; } = 3;

    public int MaxInputCharacters { get; set; } = 6000;

    public int MaxOutputTokens { get; set; } = 500;

    public int TimeoutSeconds { get; set; } = 20;

    public DateTime UpdatedAt { get; set; }

    public long? UpdatedByUserId { get; set; }
}
