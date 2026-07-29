namespace ServerOperations.Core.Models.Settings;

public enum BackupStatus
{
    Running = 0,
    Succeeded = 1,
    Failed = 2,
}

/// <summary>
/// バックアップ実行の記録。失敗はHigh通知の対象とする。
/// </summary>
public class BackupRun
{
    public long Id { get; set; }

    public BackupStatus Status { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    /// <summary>保存先のオブジェクトキー(接続情報・資格情報は含めない)。</summary>
    public string? ObjectKey { get; set; }

    /// <summary>暗号化後のサイズ(バイト)。</summary>
    public long? SizeBytes { get; set; }

    /// <summary>結果の要約(秘密情報を含めない)。</summary>
    public string? Message { get; set; }

    /// <summary>手動実行した利用者(定期実行の場合はnull)。</summary>
    public long? TriggeredByUserId { get; set; }
}
