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
