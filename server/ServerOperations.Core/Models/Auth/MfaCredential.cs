namespace ServerOperations.Core.Models.Auth;

/// <summary>
/// TOTP資格情報。共有シークレットはASP.NET Core Data Protectionで暗号化して保存する。
/// </summary>
public class MfaCredential
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Data Protectionで暗号化済みのTOTPシークレット(Base32)。</summary>
    public required string SecretProtected { get; set; }

    /// <summary>初回のTOTP検証が完了して有効化されたか。</summary>
    public bool IsEnabled { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>MFA直近認証時刻。管理操作の再認証ポリシーで参照する。</summary>
    public DateTime? LastVerifiedAt { get; set; }
}
