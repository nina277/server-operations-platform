namespace ServerOperations.Api.Models.Settings;

public enum SettingCategory
{
    Profile = 0,
    Retention = 1,
    Notification = 2,
    Ai = 3,
    Backup = 4,
}

/// <summary>
/// カテゴリ別のシステム設定。値はJSONで保持する。秘密値はここに保存せずEncryptedSecretを使う。
/// </summary>
public class SystemSetting
{
    public long Id { get; set; }

    public SettingCategory Category { get; set; }

    /// <summary>設定内容(JSON)。</summary>
    public required string Value { get; set; }

    public DateTime UpdatedAt { get; set; }

    public long? UpdatedByUserId { get; set; }
}
