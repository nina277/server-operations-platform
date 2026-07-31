using System.ComponentModel.DataAnnotations;

namespace ServerOperations.Api.DTOs.Settings;

/// <summary>一般設定。</summary>
public record ProfileSettingsDto
{
    [Required]
    [MaxLength(100)]
    public required string SystemName { get; init; }

    /// <summary>UI既定言語 (ja / en)。</summary>
    [Required]
    [RegularExpression("^(ja|en)$", ErrorMessage = "languageは ja または en を指定してください。")]
    public required string Language { get; init; }
}

/// <summary>保持設定。プロファイルまたは個別日数で指定する。</summary>
public record RetentionSettingsDto
{
    /// <summary>compact / standard / long-term / custom。</summary>
    [Required]
    [RegularExpression("^(compact|standard|long-term|custom)$",
        ErrorMessage = "profileは compact / standard / long-term / custom のいずれかを指定してください。")]
    public required string Profile { get; init; }

    [Range(1, 3650)]
    public int MetricsDays { get; init; } = 30;

    [Range(1, 3650)]
    public int LogsDays { get; init; } = 30;

    [Range(1, 3650)]
    public int IncidentsDays { get; init; } = 365;

    /// <summary>
    /// 監査ログの保持日数。下限があるのは、痕跡を消すために
    /// 保持期間を縮める操作を成立させないため。
    /// </summary>
    [Range(ServerOperations.Core.Services.RetentionPolicy.MinAuditDays, 3650,
        ErrorMessage = "監査ログの保持は90日以上にしてください。改ざんの証跡が短期間で消えるためです。")]
    public int AuditDays { get; init; } = 365;
}

public record NetworkCidrDto
{
    public required long Id { get; init; }

    public required string Cidr { get; init; }

    public string? Description { get; init; }

    public required DateTime CreatedAt { get; init; }
}

public record CreateNetworkCidrRequest
{
    [Required]
    [MaxLength(64)]
    public required string Cidr { get; init; }

    [MaxLength(200)]
    public string? Description { get; init; }
}

/// <summary>秘密値の状態。値そのものは決して返さない。</summary>
public record SecretStatusDto
{
    public required string Kind { get; init; }

    public required bool IsConfigured { get; init; }

    public DateTime? UpdatedAt { get; init; }
}

public record UpdateSecretRequest
{
    [Required]
    [MaxLength(8000)]
    public required string Value { get; init; }
}

/// <summary>
/// 通知設定。
/// 秘密値(SMTPパスワード・FCMサービスアカウント)はここに含めず、秘密情報として別に管理する。
/// </summary>
public record NotificationSettingsDto
{
    /// <summary>この深刻度以上を通知する。Critical / High / Medium / Low。</summary>
    [Required]
    [RegularExpression("^(Critical|High|Medium|Low)$",
        ErrorMessage = "minimumSeverityは Critical / High / Medium / Low のいずれかを指定してください。")]
    public required string MinimumSeverity { get; init; }

    /// <summary>同じ事象の再通知間隔(分)。この間隔内は既存の通知へまとめる。</summary>
    [Range(1, 10080)]
    public int RenotifyIntervalMinutes { get; init; } = 60;

    public bool EmailEnabled { get; init; }

    /// <summary>メールの送信先。</summary>
    public List<string> EmailRecipients { get; init; } = [];

    /// <summary>SMTPサーバーのホスト名またはIP。保存時に接続先として妥当かを検証する。</summary>
    [MaxLength(255)]
    public string? SmtpHost { get; init; }

    [Range(1, 65535)]
    public int SmtpPort { get; init; } = 587;

    public bool SmtpUseStartTls { get; init; } = true;

    [MaxLength(255)]
    public string? SmtpUsername { get; init; }

    [MaxLength(255)]
    public string? SmtpFromAddress { get; init; }

    public bool PushEnabled { get; init; }

    /// <summary>Push送信の連続失敗がこの回数に達した端末を失効させる。</summary>
    [Range(1, 100)]
    public int PushFailureThreshold { get; init; } = 3;
}

/// <summary>
/// バックアップ設定。
/// アクセスキー・シークレットキーはここに含めず、秘密情報として別に管理する。
/// </summary>
public record BackupSettingsDto
{
    public bool Enabled { get; init; }

    /// <summary>S3互換の保存先(MinIO等)。http/httpsのURL。保存時に接続先として妥当かを検証する。</summary>
    [MaxLength(500)]
    public string? Endpoint { get; init; }

    [MaxLength(100)]
    public string? BucketName { get; init; }

    /// <summary>オブジェクトキーの接頭辞。</summary>
    [MaxLength(100)]
    public string Prefix { get; init; } = "server-operations/";

    [MaxLength(64)]
    public string Region { get; init; } = "us-east-1";

    /// <summary>MinIO等ではパス形式のアクセスが必要。</summary>
    public bool UsePathStyle { get; init; } = true;

    /// <summary>保持する世代数。超えた分は削除する。</summary>
    [Range(1, 365)]
    public int KeepGenerations { get; init; } = 7;
}
