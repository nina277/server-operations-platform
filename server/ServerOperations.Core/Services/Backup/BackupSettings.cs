namespace ServerOperations.Core.Services.Backup;

/// <summary>
/// バックアップ設定。SystemSetting(カテゴリ=Backup)にJSONで保存する。
/// アクセスキー・シークレットキーはEncryptedSecretで別管理する。
/// </summary>
public class BackupSettings
{
    public bool Enabled { get; set; }

    /// <summary>S3互換エンドポイント(MinIO等)。http/httpsのURL。</summary>
    public string? Endpoint { get; set; }

    public string? BucketName { get; set; }

    /// <summary>オブジェクトキーの接頭辞。</summary>
    public string Prefix { get; set; } = "server-operations/";

    public string Region { get; set; } = "us-east-1";

    /// <summary>MinIO等ではパス形式のアクセスが必要。</summary>
    public bool UsePathStyle { get; set; } = true;

    /// <summary>バックアップの保持世代数。超えた分は削除する。</summary>
    public int KeepGenerations { get; set; } = 7;
}
