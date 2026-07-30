using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;
using ServerOperations.Core.Services.Notifications;

namespace ServerOperations.Api.Tests;

public class NotificationServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeNotificationRepository _notifications = new();
    private readonly FakeNotificationSettingsProvider _settings = new();
    private readonly RecordingChannel _channel = new();
    private readonly TestTimeProvider _time = new(BaseTime);
    private readonly FakeMaintenanceService _maintenance = new();

    private NotificationService CreateSut(params INotificationChannelSender[] channels) => new(
        _notifications, _settings, channels.Length == 0 ? [_channel] : channels, _maintenance, _time,
        NullLogger<NotificationService>.Instance);

    private static NotificationRequest Request(
        NotificationSeverity severity = NotificationSeverity.High,
        string? aggregationKey = "sig-a") => new()
    {
        Severity = severity,
        Title = "コンテナ web が停止しています",
        Body = "分類: ContainerStopped / 対象: web",
        AggregationKey = aggregationKey,
        IncidentId = 1,
        TargetId = 1,
    };

    [Fact]
    public async Task Notify_CreatesInAppDeliveryAndSendsChannels()
    {
        var notification = await CreateSut().NotifyAsync(Request());

        Assert.NotNull(notification);
        Assert.Contains(notification.Deliveries,
            d => d.Channel == NotificationChannel.InApp && d.Status == NotificationDeliveryStatus.Sent);
        Assert.Single(_channel.Sent);
    }

    [Fact]
    public async Task Notify_BelowMinimumSeverity_IsSkipped()
    {
        _settings.Settings.MinimumSeverity = NotificationSeverity.High;

        var notification = await CreateSut().NotifyAsync(Request(NotificationSeverity.Low));

        Assert.Null(notification);
        Assert.Empty(_notifications.Notifications);
        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public async Task Notify_SameKeyWithinInterval_AggregatesWithoutResending()
    {
        var sut = CreateSut();
        await sut.NotifyAsync(Request());

        _time.Now = BaseTime.AddMinutes(30);
        var second = await sut.NotifyAsync(Request());

        Assert.NotNull(second);
        Assert.Single(_notifications.Notifications);
        Assert.Equal(2, second.OccurrenceCount);
        // 集約された場合は外部チャネルへ再送しない
        Assert.Single(_channel.Sent);
    }

    [Fact]
    public async Task Notify_SameKeyAfterInterval_CreatesNewNotification()
    {
        var sut = CreateSut();
        await sut.NotifyAsync(Request());

        _time.Now = BaseTime.AddMinutes(90);
        await sut.NotifyAsync(Request());

        Assert.Equal(2, _notifications.Notifications.Count);
        Assert.Equal(2, _channel.Sent.Count);
    }

    [Fact]
    public async Task Notify_AggregationRaisesSeverity()
    {
        _settings.Settings.MinimumSeverity = NotificationSeverity.Low;
        var sut = CreateSut();
        await sut.NotifyAsync(Request(NotificationSeverity.Medium));

        _time.Now = BaseTime.AddMinutes(10);
        var second = await sut.NotifyAsync(Request(NotificationSeverity.Critical));

        Assert.NotNull(second);
        Assert.Equal(NotificationSeverity.Critical, second.Severity);
    }

    [Fact]
    public async Task Notify_WithoutAggregationKey_AlwaysCreatesNew()
    {
        var sut = CreateSut();
        await sut.NotifyAsync(Request(aggregationKey: null));
        await sut.NotifyAsync(Request(aggregationKey: null));

        Assert.Equal(2, _notifications.Notifications.Count);
    }

    [Fact]
    public async Task Notify_ChannelFailure_IsRecordedWithoutBlockingOthers()
    {
        var failing = new RecordingChannel
        {
            Channel = NotificationChannel.Email,
            Result = ChannelSendResult.Fail("メール送信に失敗しました。"),
        };
        var working = new RecordingChannel { Channel = NotificationChannel.Push };

        var notification = await CreateSut(failing, working).NotifyAsync(Request());

        Assert.NotNull(notification);
        Assert.Contains(notification.Deliveries,
            d => d.Channel == NotificationChannel.Email && d.Status == NotificationDeliveryStatus.Failed);
        Assert.Contains(notification.Deliveries,
            d => d.Channel == NotificationChannel.Push && d.Status == NotificationDeliveryStatus.Sent);
        Assert.Single(working.Sent);
    }

    [Fact]
    public async Task Notify_ChannelThrows_IsRecordedAsFailed()
    {
        var throwing = new ThrowingChannel();

        var notification = await CreateSut(throwing).NotifyAsync(Request());

        Assert.NotNull(notification);
        Assert.Contains(notification.Deliveries,
            d => d.Channel == NotificationChannel.Email && d.Status == NotificationDeliveryStatus.Failed);
    }

    [Fact]
    public async Task Notify_SkippedChannel_IsRecordedAsSkipped()
    {
        var skipping = new RecordingChannel
        {
            Channel = NotificationChannel.Email,
            Result = ChannelSendResult.Skip("メール通知は無効です。"),
        };

        var notification = await CreateSut(skipping).NotifyAsync(Request());

        Assert.NotNull(notification);
        Assert.Contains(notification.Deliveries,
            d => d.Channel == NotificationChannel.Email && d.Status == NotificationDeliveryStatus.Skipped);
    }

    // --- メンテナンス期間中の抑止 ---

    [Fact]
    public async Task メンテナンス期間中は通知しない()
    {
        // 計画停止中に知らせても対応するものが無く、本当の障害が埋もれる
        _maintenance.State = new MaintenanceState
        {
            SuppressNotifications = true,
            Reason = "カーネル更新",
        };

        var notification = await CreateSut().NotifyAsync(Request());

        Assert.Null(notification);
        Assert.Empty(_channel.Sent);
    }

    [Fact]
    public async Task メンテナンス期間中でも自動復旧だけを止める設定なら通知する()
    {
        _maintenance.State = new MaintenanceState
        {
            SuppressNotifications = false,
            SuppressAutoRecovery = true,
            Reason = "カーネル更新",
        };

        var notification = await CreateSut().NotifyAsync(Request());

        Assert.NotNull(notification);
    }

    [Fact]
    public async Task 対象が特定できない通知は抑止の判定をしない()
    {
        // 対象なしの通知(システム全体の知らせ)まで止めると、
        // メンテナンス中にシステム自身の異常を知らせられなくなる
        _maintenance.State = new MaintenanceState { SuppressNotifications = true };

        var notification = await CreateSut().NotifyAsync(Request() with { TargetId = null });

        Assert.NotNull(notification);
        Assert.Empty(_maintenance.QueriedTargetIds);
    }

    private sealed class RecordingChannel : INotificationChannelSender
    {
        public NotificationChannel Channel { get; set; } = NotificationChannel.Email;

        public ChannelSendResult Result { get; set; } = ChannelSendResult.Ok();

        public List<Notification> Sent { get; } = [];

        public Task<ChannelSendResult> SendAsync(Notification notification, CancellationToken ct = default)
        {
            if (Result.Success)
            {
                Sent.Add(notification);
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class ThrowingChannel : INotificationChannelSender
    {
        public NotificationChannel Channel => NotificationChannel.Email;

        public Task<ChannelSendResult> SendAsync(Notification notification, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");
    }
}

public class FakeNotificationRepository : INotificationRepository
{
    public List<Notification> Notifications { get; } = [];

    public Task<Notification?> FindRecentByAggregationKeyAsync(
        string aggregationKey, DateTime notifiedAfterUtc, CancellationToken ct = default) =>
        Task.FromResult(Notifications
            .Where(n => n.AggregationKey == aggregationKey && n.LastNotifiedAt >= notifiedAfterUtc)
            .OrderByDescending(n => n.LastNotifiedAt)
            .FirstOrDefault());

    public Task<Notification?> FindByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Notifications.FirstOrDefault(n => n.Id == id));

    public Task<(List<Notification> Items, long TotalCount)> SearchAsync(
        bool? isRead, int page, int pageSize, CancellationToken ct = default)
    {
        var query = Notifications.AsEnumerable();
        if (isRead is { } read)
        {
            query = query.Where(n => n.IsRead == read);
        }

        var list = query.OrderByDescending(n => n.LastNotifiedAt).ToList();
        return Task.FromResult((
            list.Skip((Math.Max(page, 1) - 1) * pageSize).Take(pageSize).ToList(),
            (long)list.Count));
    }

    public Task<int> CountUnreadAsync(CancellationToken ct = default) =>
        Task.FromResult(Notifications.Count(n => !n.IsRead));

    public Task AddAsync(Notification notification, CancellationToken ct = default)
    {
        notification.Id = Notifications.Count + 1;
        Notifications.Add(notification);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeNotificationSettingsProvider : INotificationSettingsProvider
{
    public NotificationSettings Settings { get; } = new()
    {
        MinimumSeverity = NotificationSeverity.Medium,
        RenotifyIntervalMinutes = 60,
    };

    public string? SmtpPassword { get; set; }

    public string? FcmServiceAccountJson { get; set; }

    public Task<NotificationSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(Settings);

    public Task<string?> GetSmtpPasswordAsync(CancellationToken ct = default) => Task.FromResult(SmtpPassword);

    public Task<string?> GetFcmServiceAccountJsonAsync(CancellationToken ct = default) =>
        Task.FromResult(FcmServiceAccountJson);
}
