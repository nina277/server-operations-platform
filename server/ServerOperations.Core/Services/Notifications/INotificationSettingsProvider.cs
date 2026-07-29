namespace ServerOperations.Core.Services.Notifications;

/// <summary>
/// 通知設定と、通知に必要な秘密値の取得。
/// 秘密値は呼び出し時に復号し、保持・ログ出力しない。
/// </summary>
public interface INotificationSettingsProvider
{
    Task<NotificationSettings> GetAsync(CancellationToken ct = default);

    /// <summary>SMTPパスワード(未設定ならnull)。</summary>
    Task<string?> GetSmtpPasswordAsync(CancellationToken ct = default);

    /// <summary>FCMサービスアカウントJSON(未設定ならnull)。</summary>
    Task<string?> GetFcmServiceAccountJsonAsync(CancellationToken ct = default);
}
