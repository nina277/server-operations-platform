namespace ServerOperations.Core.Models.Auth;

public class User
{
    public long Id { get; set; }

    public required string Username { get; set; }

    /// <summary>BCryptハッシュ。平文パスワードは保持しない。</summary>
    public required string PasswordHash { get; set; }

    public UserRole Role { get; set; } = UserRole.Viewer;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public MfaCredential? MfaCredential { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];
}
