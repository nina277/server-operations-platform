using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Repositories.Interfaces;
using CoreNotification = ServerOperations.Core.Models.Operations.Notification;
using NotificationChannel = ServerOperations.Core.Models.Operations.NotificationChannel;

namespace ServerOperations.Core.Services.Notifications;

/// <summary>
/// Web Push通知(FCM)。送信に失敗した端末トークンは連続失敗回数を数え、閾値で失効させる。
/// </summary>
public class PushNotificationSender(
    INotificationSettingsProvider settingsProvider,
    IDeviceTokenRepository deviceTokens,
    TimeProvider timeProvider,
    ILogger<PushNotificationSender> logger) : INotificationChannelSender
{
    private const string FirebaseAppName = "server-operations-platform";

    public NotificationChannel Channel => NotificationChannel.Push;

    public async Task<ChannelSendResult> SendAsync(CoreNotification notification, CancellationToken ct = default)
    {
        var settings = await settingsProvider.GetAsync(ct);
        if (!settings.PushEnabled)
        {
            return ChannelSendResult.Skip("Push通知は無効です。");
        }

        var serviceAccountJson = await settingsProvider.GetFcmServiceAccountJsonAsync(ct);
        if (string.IsNullOrWhiteSpace(serviceAccountJson))
        {
            return ChannelSendResult.Skip("FCMサービスアカウントが設定されていません。");
        }

        var tokens = await deviceTokens.GetActiveAsync(ct);
        if (tokens.Count == 0)
        {
            return ChannelSendResult.Skip("送信先の端末が登録されていません。");
        }

        FirebaseMessaging messaging;
        try
        {
            messaging = GetMessaging(serviceAccountJson);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to initialize Firebase messaging.");
            return ChannelSendResult.Fail("FCMの初期化に失敗しました。");
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var anySuccess = false;
        var failureCount = 0;

        foreach (var deviceToken in tokens)
        {
            try
            {
                await messaging.SendAsync(new Message
                {
                    Token = deviceToken.Token,
                    Notification = new FirebaseAdmin.Messaging.Notification
                    {
                        Title = $"[{notification.Severity}] {notification.Title}",
                        Body = notification.Body,
                    },
                    Data = new Dictionary<string, string>
                    {
                        ["notificationId"] = notification.Id.ToString(),
                        ["severity"] = notification.Severity.ToString(),
                        ["incidentId"] = notification.IncidentId?.ToString() ?? string.Empty,
                    },
                }, ct);

                deviceToken.ConsecutiveFailureCount = 0;
                deviceToken.LastUsedAt = now;
                anySuccess = true;
            }
            catch (FirebaseMessagingException ex)
            {
                failureCount++;
                deviceToken.ConsecutiveFailureCount++;

                // 無効・未登録のトークンは即座に失効させる
                var permanentlyInvalid = ex.MessagingErrorCode
                    is MessagingErrorCode.Unregistered or MessagingErrorCode.InvalidArgument;

                if (permanentlyInvalid || deviceToken.ConsecutiveFailureCount >= settings.PushFailureThreshold)
                {
                    deviceToken.IsActive = false;
                    deviceToken.RevokedAt = now;
                    logger.LogInformation(
                        "Revoked device token {TokenId} after push failure (permanent={Permanent}).",
                        deviceToken.Id, permanentlyInvalid);
                }
            }
        }

        await deviceTokens.SaveChangesAsync(ct);

        if (anySuccess)
        {
            return ChannelSendResult.Ok();
        }

        return ChannelSendResult.Fail($"すべての端末({failureCount}件)への送信に失敗しました。");
    }

    /// <summary>
    /// FirebaseAppは名前付きで1つだけ生成し、以降は再利用する。
    /// </summary>
    private static FirebaseMessaging GetMessaging(string serviceAccountJson)
    {
        var app = FirebaseApp.GetInstance(FirebaseAppName)
            ?? FirebaseApp.Create(new AppOptions
            {
                Credential = GoogleCredential.FromJson(serviceAccountJson),
            }, FirebaseAppName);

        return FirebaseMessaging.GetMessaging(app);
    }
}
