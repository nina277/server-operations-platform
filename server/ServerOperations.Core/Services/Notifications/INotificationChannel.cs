using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services.Notifications;

public record ChannelSendResult(bool Success, string? ErrorMessage = null, bool Skipped = false)
{
    public static ChannelSendResult Ok() => new(true);

    public static ChannelSendResult Fail(string error) => new(false, error);

    /// <summary>チャネル未設定・無効のため送信しなかった。</summary>
    public static ChannelSendResult Skip(string reason) => new(false, reason, Skipped: true);
}

/// <summary>
/// 通知チャネル。実装は本文へ秘密情報・ログ全文を含めないこと。
/// </summary>
public interface INotificationChannelSender
{
    NotificationChannel Channel { get; }

    Task<ChannelSendResult> SendAsync(Notification notification, CancellationToken ct = default);
}
