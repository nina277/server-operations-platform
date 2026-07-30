using System.ComponentModel.DataAnnotations;

namespace ServerOperations.Api.DTOs.Auth;

public record LoginRequest
{
    [Required]
    [MaxLength(64)]
    public required string Username { get; init; }

    [Required]
    [MaxLength(128)]
    public required string Password { get; init; }

    /// <summary>MFA有効ユーザーの場合に必須となるTOTPコード。</summary>
    [MaxLength(8)]
    public string? TotpCode { get; init; }
}

public record TokenPairResponse
{
    public required string AccessToken { get; init; }

    public required DateTime AccessTokenExpiresAt { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTime RefreshTokenExpiresAt { get; init; }
}

public record RefreshRequest
{
    [Required]
    public required string RefreshToken { get; init; }
}

public record LogoutRequest
{
    [Required]
    public required string RefreshToken { get; init; }
}

public record MfaSetupResponse
{
    /// <summary>Base32のTOTPシークレット。この応答でのみ返し、以後は再表示しない。</summary>
    public required string Secret { get; init; }

    /// <summary>認証アプリ登録用のotpauth URI。</summary>
    public required string OtpAuthUri { get; init; }
}

public record MfaVerifyRequest
{
    [Required]
    [MaxLength(8)]
    public required string TotpCode { get; init; }
}

public record MfaVerifyResponse
{
    public required bool MfaEnabled { get; init; }

    public required DateTime VerifiedAt { get; init; }
}

public record MeResponse
{
    public required long Id { get; init; }

    public required string Username { get; init; }

    public required string Role { get; init; }

    public required bool MfaEnabled { get; init; }
}

public record ChangePasswordRequest
{
    /// <summary>本人確認のため、現在のパスワードを要求する。</summary>
    [Required]
    [MaxLength(128)]
    public required string CurrentPassword { get; init; }

    /// <summary>新しいパスワード。初期管理者と同じ12文字以上を要求する。</summary>
    [Required]
    [MinLength(12)]
    [MaxLength(128)]
    public required string NewPassword { get; init; }
}

public record ChangePasswordResponse
{
    public required DateTime ChangedAt { get; init; }

    /// <summary>
    /// 変更に伴って他の端末のセッションを切ったか。
    /// 漏えい後の変更で古いセッションが残らないようにする。
    /// </summary>
    public required bool OtherSessionsRevoked { get; init; }
}
