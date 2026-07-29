using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services.Notifications;

/// <summary>
/// 通知設定。SystemSetting(カテゴリ=Notification)にJSONで保存する。
/// 秘密値(SMTPパスワード・FCMサービスアカウント)はEncryptedSecretで別管理する。
/// </summary>
public class NotificationSettings
{
    /// <summary>この重大度以上を通知する。</summary>
    public NotificationSeverity MinimumSeverity { get; set; } = NotificationSeverity.Medium;

    /// <summary>同一集約キーの再通知間隔(分)。この間隔内は既存通知へ集約する。</summary>
    public int RenotifyIntervalMinutes { get; set; } = 60;

    public bool EmailEnabled { get; set; }

    /// <summary>メール送信先(カンマ区切りではなく配列で保持)。</summary>
    public List<string> EmailRecipients { get; set; } = [];

    public string? SmtpHost { get; set; }

    public int SmtpPort { get; set; } = 587;

    public bool SmtpUseStartTls { get; set; } = true;

    public string? SmtpUsername { get; set; }

    public string? SmtpFromAddress { get; set; }

    public bool PushEnabled { get; set; }

    /// <summary>Push送信の連続失敗がこの回数に達した端末トークンを失効させる。</summary>
    public int PushFailureThreshold { get; set; } = 3;
}
