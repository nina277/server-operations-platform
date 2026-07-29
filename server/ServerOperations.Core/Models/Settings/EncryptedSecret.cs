namespace ServerOperations.Core.Models.Settings;

/// <summary>
/// 暗号化された秘密値(SMTPパスワード、AI APIキー等)。
/// 値はData Protectionで暗号化して保存し、取得APIでは設定済みか否かと更新日時だけを返す。
/// </summary>
public class EncryptedSecret
{
    public long Id { get; set; }

    /// <summary>秘密値の種別(例: smtp-password, gemini-api-key)。許可リストで検証する。</summary>
    public required string Kind { get; set; }

    /// <summary>Data Protectionで暗号化済みの値。</summary>
    public required string ValueProtected { get; set; }

    public DateTime UpdatedAt { get; set; }

    public long? UpdatedByUserId { get; set; }
}
