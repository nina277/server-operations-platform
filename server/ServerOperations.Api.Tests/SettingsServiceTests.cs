using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;

namespace ServerOperations.Api.Tests;

public class SettingsServiceTests
{
    private readonly FakeSystemSettingRepository _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));

    private readonly FakeNotificationTestService _notificationTest = new();

    private SettingsService CreateSut() =>
        new(_repo, _audit, _currentUser, _notificationTest, _time);

    [Fact]
    public async Task GetProfile_ReturnsDefaults_WhenUnset()
    {
        var sut = CreateSut();

        var profile = await sut.GetProfileAsync();

        Assert.Equal("Server Operations Platform", profile.SystemName);
        Assert.Equal("ja", profile.Language);
    }

    [Fact]
    public async Task UpdateProfile_PersistsAndAuditsWithBeforeAfterSummary()
    {
        var sut = CreateSut();

        var updated = await sut.UpdateProfileAsync(new ProfileSettingsDto
        {
            SystemName = "Lab Ops",
            Language = "en",
        });

        Assert.Equal("Lab Ops", updated.SystemName);
        var roundTrip = await sut.GetProfileAsync();
        Assert.Equal("Lab Ops", roundTrip.SystemName);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("settings.profile.update", entry.Action);
        Assert.Equal(AuditResult.Success, entry.Result);
        Assert.Equal(1, entry.ActorUserId);
    }

    [Fact]
    public async Task UpdateRetention_RoundTrips()
    {
        var sut = CreateSut();

        await sut.UpdateRetentionAsync(new RetentionSettingsDto
        {
            Profile = "custom",
            MetricsDays = 7,
            LogsDays = 14,
            IncidentsDays = 180,
            AuditDays = 730,
        });

        var stored = await sut.GetRetentionAsync();
        Assert.Equal("custom", stored.Profile);
        Assert.Equal(7, stored.MetricsDays);
        Assert.Equal(730, stored.AuditDays);
    }

    // --- 通知設定 ---

    private static NotificationSettingsDto Notification(
        bool emailEnabled = false,
        string? smtpHost = "smtp.example.com",
        int smtpPort = 587,
        string? fromAddress = "ops@example.com",
        List<string>? recipients = null,
        bool pushEnabled = false) => new()
    {
        MinimumSeverity = "Medium",
        RenotifyIntervalMinutes = 60,
        EmailEnabled = emailEnabled,
        EmailRecipients = recipients ?? ["admin@example.com"],
        SmtpHost = smtpHost,
        SmtpPort = smtpPort,
        SmtpUseStartTls = true,
        SmtpFromAddress = fromAddress,
        PushEnabled = pushEnabled,
        PushFailureThreshold = 3,
    };

    [Fact]
    public async Task 通知設定は未設定なら既定値を返す()
    {
        var settings = await CreateSut().GetNotificationAsync();

        // 既定では通知を送らない(設定するまで勝手に送信しない)
        Assert.False(settings.EmailEnabled);
        Assert.False(settings.PushEnabled);
        Assert.Equal("Medium", settings.MinimumSeverity);
        Assert.Equal(60, settings.RenotifyIntervalMinutes);
    }

    [Fact]
    public async Task 通知設定を保存して読み直せる()
    {
        var sut = CreateSut();

        await sut.UpdateNotificationAsync(Notification(pushEnabled: true));
        var saved = await sut.GetNotificationAsync();

        Assert.True(saved.PushEnabled);
        Assert.Equal("smtp.example.com", saved.SmtpHost);
    }

    [Fact]
    public async Task 通知設定の変更を監査に残す()
    {
        await CreateSut().UpdateNotificationAsync(Notification(pushEnabled: true));

        Assert.Contains(_audit.Entries, e => e.Action == "settings.notification.update");
    }

    [Fact]
    public async Task メール通知を有効にするならSMTPサーバーが必要()
    {
        // 有効にしたのに送信できない、という状態で保存させない
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateNotificationAsync(Notification(emailEnabled: true, smtpHost: null)));

        Assert.Equal("smtp_host_required", ex.Code);
    }

    [Fact]
    public async Task メール通知を有効にするなら送信元が必要()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateNotificationAsync(Notification(emailEnabled: true, fromAddress: null)));

        Assert.Equal("smtp_from_required", ex.Code);
    }

    [Fact]
    public async Task メール通知を有効にするなら送信先が必要()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateNotificationAsync(Notification(emailEnabled: true, recipients: [])));

        Assert.Equal("email_recipients_required", ex.Code);
    }

    [Fact]
    public async Task 空白だけの送信先は指定と見なさない()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateNotificationAsync(
                Notification(emailEnabled: true, recipients: ["  ", ""])));

        Assert.Equal("email_recipients_required", ex.Code);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("missing-domain@")]
    [InlineData("@missing-local.example.com")]
    [InlineData("two@at@example.com")]
    [InlineData("no-dot@localhost")]
    [InlineData("with space@example.com")]
    public async Task 形式が不正なメールアドレスは拒否する(string address)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateNotificationAsync(
                Notification(emailEnabled: true, recipients: [address])));

        Assert.Equal("invalid_email_address", ex.Code);
    }

    [Fact]
    public async Task メール通知が無効なら送信先が無くても保存できる()
    {
        // まだ設定途中の状態を保存できるようにする
        var saved = await CreateSut().UpdateNotificationAsync(
            Notification(emailEnabled: false, smtpHost: null, fromAddress: null, recipients: []));

        Assert.False(saved.EmailEnabled);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("169.254.169.254")]
    public async Task 遮断対象のSMTPサーバーは拒否する(string host)
    {
        // 任意のホストへ接続させない。URLと同じ基準を適用する。
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateNotificationAsync(
                Notification(emailEnabled: true, smtpHost: host)));

        // 3件はいずれもDNSを引かずに判定できるため、コードは一意に決まる
        Assert.Equal("url_not_allowed", ex.Code);
    }

    [Fact]
    public async Task SMTPサーバーにURLを入れさせない()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateNotificationAsync(
                Notification(emailEnabled: true, smtpHost: "http://smtp.example.com/path")));

        Assert.Equal("invalid_host", ex.Code);
    }

    // --- バックアップ設定 ---

    private static BackupSettingsDto Backup(
        bool enabled = false,
        string? endpoint = "http://minio.example.com:9000",
        string? bucket = "backups") => new()
    {
        Enabled = enabled,
        Endpoint = endpoint,
        BucketName = bucket,
        Prefix = "server-operations/",
        Region = "us-east-1",
        UsePathStyle = true,
        KeepGenerations = 7,
    };

    [Fact]
    public async Task バックアップ設定は未設定なら既定値を返す()
    {
        var settings = await CreateSut().GetBackupAsync();

        // 既定では無効(保存先を決めるまで動かさない)
        Assert.False(settings.Enabled);
        Assert.Equal(7, settings.KeepGenerations);
        Assert.True(settings.UsePathStyle);
    }

    [Fact]
    public async Task バックアップ設定を保存して読み直せる()
    {
        var sut = CreateSut();

        await sut.UpdateBackupAsync(Backup(enabled: false, endpoint: null, bucket: null) with
        {
            KeepGenerations = 30,
        });
        var saved = await sut.GetBackupAsync();

        Assert.Equal(30, saved.KeepGenerations);
    }

    [Fact]
    public async Task バックアップの変更を監査に残す()
    {
        await CreateSut().UpdateBackupAsync(Backup());

        Assert.Contains(_audit.Entries, e => e.Action == "settings.backup.update");
    }

    [Fact]
    public async Task バックアップを有効にするなら保存先が必要()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateBackupAsync(Backup(enabled: true, endpoint: null)));

        Assert.Equal("backup_endpoint_required", ex.Code);
    }

    [Fact]
    public async Task バックアップを有効にするならバケット名が必要()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateBackupAsync(Backup(enabled: true, bucket: null)));

        Assert.Equal("backup_bucket_required", ex.Code);
    }

    [Theory]
    [InlineData("http://localhost:9000")]
    [InlineData("http://127.0.0.1:9000")]
    [InlineData("http://169.254.169.254/")]
    public async Task 遮断対象の保存先は拒否する(string endpoint)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateBackupAsync(Backup(enabled: true, endpoint: endpoint)));

        Assert.Equal("url_not_allowed", ex.Code);
    }

    [Fact]
    public async Task 保存先にHTTP以外は指定できない()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateBackupAsync(Backup(enabled: true, endpoint: "file:///etc/passwd")));

        Assert.Equal("invalid_url_scheme", ex.Code);
    }

    [Fact]
    public async Task バックアップが無効なら保存先が無くても保存できる()
    {
        var saved = await CreateSut().UpdateBackupAsync(
            Backup(enabled: false, endpoint: null, bucket: null));

        Assert.False(saved.Enabled);
    }
}
