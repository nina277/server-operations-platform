namespace ServerOperations.Api.Models.Auth;

/// <summary>
/// リフレッシュトークン。トークン値そのものは保存せず、SHA-256ハッシュのみ保持する。
/// ローテーション方式: 使用のたびに新トークンを発行し、旧トークンを失効させる。
/// FamilyIdはローテーション系列を表し、失効済みトークンの再利用を検出した場合は系列全体を失効させる。
/// </summary>
public class RefreshToken
{
    public long Id { get; set; }

    public long UserId { get; set; }

    public User? User { get; set; }

    /// <summary>トークン値のSHA-256ハッシュ(Base64)。</summary>
    public required string TokenHash { get; set; }

    /// <summary>ローテーション系列ID。</summary>
    public Guid FamilyId { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedByIp { get; set; }

    /// <summary>失効日時。ローテーションで置き換えられた時、ログアウト時、系列失効時に設定される。</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>ローテーションで置き換えた後継トークンのハッシュ。</summary>
    public string? ReplacedByTokenHash { get; set; }

    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAt;

    public bool IsActive(DateTime nowUtc) => RevokedAt is null && !IsExpired(nowUtc);
}
