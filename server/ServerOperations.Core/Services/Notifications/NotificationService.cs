using Microsoft.Extensions.Logging;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services.Notifications;

public record NotificationRequest
{
    public required NotificationSeverity Severity { get; init; }

    public required string Title { get; init; }

    /// <summary>本文。ログ全文・秘密情報を渡さないこと(呼び出し側の責務)。</summary>
    public required string Body { get; init; }

    /// <summary>集約キー(障害署名など)。同一キーは再通知間隔内で集約する。</summary>
    public string? AggregationKey { get; init; }

    public long? IncidentId { get; init; }

    public long? TargetId { get; init; }
}

public interface INotificationService
{
    /// <summary>
    /// 通知する。重大度が下限未満なら何もしない。
    /// 同一集約キーの通知が再通知間隔内にあれば、新規作成せず既存へ集約する。
    /// </summary>
    Task<Notification?> NotifyAsync(NotificationRequest request, CancellationToken ct = default);
}

public class NotificationService(
    INotificationRepository notifications,
    INotificationSettingsProvider settingsProvider,
    IEnumerable<INotificationChannelSender> channels,
    TimeProvider timeProvider,
    ILogger<NotificationService> logger) : INotificationService
{
    public async Task<Notification?> NotifyAsync(NotificationRequest request, CancellationToken ct = default)
    {
        var settings = await settingsProvider.GetAsync(ct);

        if (request.Severity < settings.MinimumSeverity)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 集約: 再通知間隔内に同一キーの通知があれば、回数を加算して外部送信しない
        if (!string.IsNullOrEmpty(request.AggregationKey))
        {
            var threshold = now.AddMinutes(-settings.RenotifyIntervalMinutes);
            var existing = await notifications.FindRecentByAggregationKeyAsync(
                request.AggregationKey, threshold, ct);

            if (existing is not null)
            {
                existing.OccurrenceCount++;
                existing.LastNotifiedAt = now;

                // 重大度が上がった場合は引き上げる
                if (request.Severity > existing.Severity)
                {
                    existing.Severity = request.Severity;
                }

                await notifications.SaveChangesAsync(ct);
                return existing;
            }
        }

        var notification = new Notification
        {
            Severity = request.Severity,
            Title = TruncateRequired(request.Title, 200),
            Body = TruncateRequired(request.Body, 2000),
            AggregationKey = request.AggregationKey,
            IncidentId = request.IncidentId,
            TargetId = request.TargetId,
            OccurrenceCount = 1,
            FirstNotifiedAt = now,
            LastNotifiedAt = now,
        };

        // 画面内通知は常に記録する
        notification.Deliveries.Add(new NotificationDelivery
        {
            Channel = NotificationChannel.InApp,
            Status = NotificationDeliveryStatus.Sent,
            AttemptedAt = now,
        });

        await notifications.AddAsync(notification, ct);
        await notifications.SaveChangesAsync(ct);

        // 外部チャネルは1つ失敗しても他を止めない
        foreach (var channel in channels)
        {
            var delivery = new NotificationDelivery
            {
                NotificationId = notification.Id,
                Channel = channel.Channel,
                AttemptedAt = timeProvider.GetUtcNow().UtcDateTime,
            };

            try
            {
                var result = await channel.SendAsync(notification, ct);
                delivery.Status = result switch
                {
                    { Skipped: true } => NotificationDeliveryStatus.Skipped,
                    { Success: true } => NotificationDeliveryStatus.Sent,
                    _ => NotificationDeliveryStatus.Failed,
                };
                delivery.ErrorMessage = Truncate(result.ErrorMessage, 500);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notification channel {Channel} threw an exception.", channel.Channel);
                delivery.Status = NotificationDeliveryStatus.Failed;
                delivery.ErrorMessage = "送信中にエラーが発生しました。";
            }

            notification.Deliveries.Add(delivery);
        }

        await notifications.SaveChangesAsync(ct);
        return notification;
    }

    /// <summary>任意項目の切り詰め。nullはnullのまま返す。</summary>
    private static string? Truncate(string? value, int maxLength) =>
        value is null || value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>必須項目の切り詰め。呼び出し側がnullを渡さない項目に使う。</summary>
    private static string TruncateRequired(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
