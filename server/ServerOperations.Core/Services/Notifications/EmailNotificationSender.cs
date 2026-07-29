using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using MimeKit;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services.Notifications;

/// <summary>
/// メール通知(MailKit)。SMTPパスワードはEncryptedSecretから取得し、本文・ログへ出さない。
/// </summary>
public class EmailNotificationSender(
    INotificationSettingsProvider settingsProvider,
    ILogger<EmailNotificationSender> logger) : INotificationChannelSender
{
    public NotificationChannel Channel => NotificationChannel.Email;

    public async Task<ChannelSendResult> SendAsync(Notification notification, CancellationToken ct = default)
    {
        var settings = await settingsProvider.GetAsync(ct);

        if (!settings.EmailEnabled)
        {
            return ChannelSendResult.Skip("メール通知は無効です。");
        }

        if (string.IsNullOrWhiteSpace(settings.SmtpHost)
            || string.IsNullOrWhiteSpace(settings.SmtpFromAddress)
            || settings.EmailRecipients.Count == 0)
        {
            return ChannelSendResult.Skip("メール通知の設定が不足しています。");
        }

        var password = await settingsProvider.GetSmtpPasswordAsync(ct);

        try
        {
            var message = new MimeMessage();
            message.From.Add(MailboxAddress.Parse(settings.SmtpFromAddress));
            foreach (var recipient in settings.EmailRecipients)
            {
                message.To.Add(MailboxAddress.Parse(recipient));
            }

            message.Subject = $"[{notification.Severity}] {notification.Title}";
            message.Body = new TextPart("plain") { Text = BuildBody(notification) };

            using var client = new SmtpClient();
            await client.ConnectAsync(
                settings.SmtpHost,
                settings.SmtpPort,
                settings.SmtpUseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                ct);

            if (!string.IsNullOrWhiteSpace(settings.SmtpUsername) && !string.IsNullOrWhiteSpace(password))
            {
                await client.AuthenticateAsync(settings.SmtpUsername, password, ct);
            }

            await client.SendAsync(message, ct);
            await client.DisconnectAsync(quit: true, ct);

            return ChannelSendResult.Ok();
        }
        catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException
            or AuthenticationException or IOException or OperationCanceledException)
        {
            // 例外メッセージに資格情報が含まれる可能性があるため、記録は種別のみに留める
            logger.LogWarning(ex, "Failed to send email notification.");
            return ChannelSendResult.Fail("メール送信に失敗しました。");
        }
    }

    /// <summary>通知本文。ログ全文・秘密情報は含めず、詳細は画面で確認する導線にする。</summary>
    private static string BuildBody(Notification notification)
    {
        var lines = new List<string>
        {
            notification.Body,
            string.Empty,
            $"重大度: {notification.Severity}",
            $"発生回数: {notification.OccurrenceCount}",
            $"初回: {notification.FirstNotifiedAt:yyyy-MM-dd HH:mm:ss} UTC",
            $"直近: {notification.LastNotifiedAt:yyyy-MM-dd HH:mm:ss} UTC",
        };

        if (notification.IncidentId is { } incidentId)
        {
            lines.Add($"インシデントID: {incidentId}");
        }

        lines.Add(string.Empty);
        lines.Add("詳細はシステムの画面で確認してください。");

        return string.Join(Environment.NewLine, lines);
    }
}
