using System.ComponentModel.DataAnnotations;

namespace ServerOperations.Api.DTOs.Auth;

public record ManagedUserDto
{
    public required long Id { get; init; }

    public required string Username { get; init; }

    public required string Role { get; init; }

    public required bool IsActive { get; init; }

    /// <summary>MFAが有効か。値そのものは返らない。</summary>
    public required bool MfaEnabled { get; init; }

    public required DateTime CreatedAt { get; init; }

    public required DateTime UpdatedAt { get; init; }
}

public record CreateUserRequest
{
    [Required(ErrorMessage = "ユーザー名を入力してください。")]
    [MaxLength(64, ErrorMessage = "ユーザー名は64文字以内で入力してください。")]
    [RegularExpression("^[A-Za-z0-9._-]+$",
        ErrorMessage = "ユーザー名には英数字と . _ - だけを使えます。")]
    public required string Username { get; init; }

    /// <summary>
    /// 初期パスワード。本人が最初のログイン後に変更する運用を前提とする。
    /// 応答には返さない。
    /// </summary>
    [Required(ErrorMessage = "初期パスワードを入力してください。")]
    [MinLength(12, ErrorMessage = "パスワードは12文字以上で入力してください。")]
    [MaxLength(200)]
    public required string Password { get; init; }

    [Required]
    [RegularExpression("^(Viewer|OperatorAdmin|SystemExecutor)$",
        ErrorMessage = "役割は Viewer / OperatorAdmin / SystemExecutor のいずれかを指定してください。")]
    public required string Role { get; init; }
}

public record UpdateUserRoleRequest
{
    [Required]
    [RegularExpression("^(Viewer|OperatorAdmin|SystemExecutor)$",
        ErrorMessage = "役割は Viewer / OperatorAdmin / SystemExecutor のいずれかを指定してください。")]
    public required string Role { get; init; }
}

public record UpdateUserActiveRequest
{
    public required bool IsActive { get; init; }
}
