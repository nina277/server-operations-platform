namespace ServerOperations.Core.Models.Operations;

/// <summary>
/// 収集したログ抜粋。保存前に秘密情報(トークン・パスワード等)をマスクする。
/// </summary>
public class IncidentLog
{
    public long Id { get; set; }

    public long TargetId { get; set; }

    /// <summary>関連インシデント(収集時点で特定できた場合)。</summary>
    public long? IncidentId { get; set; }

    public DateTime CollectedAt { get; set; }

    /// <summary>ログの発生源(コンテナ名等)。</summary>
    public required string Source { get; set; }

    /// <summary>マスク済みログ本文。</summary>
    public required string MaskedContent { get; set; }
}
