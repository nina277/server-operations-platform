using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services.Backup;

namespace ServerOperations.Api.Tests;

public class BackupEncryptionTests
{
    [Fact]
    public void EncryptDecrypt_RoundTrips()
    {
        var plaintext = Encoding.UTF8.GetBytes("""{"users":[{"id":1,"username":"admin"}]}""");

        var encrypted = BackupService.Encrypt(plaintext, "backup-key");
        var decrypted = BackupService.Decrypt(encrypted, "backup-key");

        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void Encrypt_DoesNotContainPlaintext()
    {
        var plaintext = Encoding.UTF8.GetBytes("secret-content-marker");

        var encrypted = BackupService.Encrypt(plaintext, "backup-key");

        Assert.DoesNotContain("secret-content-marker", Encoding.UTF8.GetString(encrypted));
    }

    [Fact]
    public void Encrypt_SameInputProducesDifferentCiphertext()
    {
        // nonceが毎回変わるため同じ入力でも出力は一致しない
        var plaintext = Encoding.UTF8.GetBytes("same-input");

        var first = BackupService.Encrypt(plaintext, "backup-key");
        var second = BackupService.Encrypt(plaintext, "backup-key");

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Decrypt_WithWrongKey_Throws()
    {
        var encrypted = BackupService.Encrypt(Encoding.UTF8.GetBytes("data"), "correct-key");

        Assert.Throws<AuthenticationTagMismatchException>(() =>
            BackupService.Decrypt(encrypted, "wrong-key"));
    }

    [Fact]
    public void Decrypt_TamperedData_Throws()
    {
        var encrypted = BackupService.Encrypt(Encoding.UTF8.GetBytes("data"), "backup-key");
        encrypted[^1] ^= 0xFF;

        Assert.Throws<AuthenticationTagMismatchException>(() =>
            BackupService.Decrypt(encrypted, "backup-key"));
    }

    [Fact]
    public void Decrypt_TooShortData_Throws()
    {
        Assert.Throws<CryptographicException>(() =>
            BackupService.Decrypt([1, 2, 3], "backup-key"));
    }
}

public class BackupServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeBackupSettingsProvider _settings = new();
    private readonly FakeBackupRunRepository _runs = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    private BackupService CreateSut() => new(
        _settings, _runs, new StubSourceProvider(), _notifications, _time,
        NullLogger<BackupService>.Instance);

    [Fact]
    public async Task Run_WithoutEndpoint_FailsAndNotifiesHigh()
    {
        _settings.Settings.Enabled = true;
        _settings.Settings.Endpoint = null;

        var run = await CreateSut().RunAsync();

        Assert.Equal(BackupStatus.Failed, run.Status);
        var notification = Assert.Single(_notifications.Requests);
        Assert.Equal(NotificationSeverity.High, notification.Severity);
        Assert.Equal("backup-failed", notification.AggregationKey);
    }

    [Fact]
    public async Task Run_Disabled_FailsAndNotifies()
    {
        _settings.Settings.Enabled = false;
        _settings.Settings.Endpoint = "http://192.168.1.50:9000";
        _settings.Settings.BucketName = "backups";

        var run = await CreateSut().RunAsync();

        Assert.Equal(BackupStatus.Failed, run.Status);
        Assert.Single(_notifications.Requests);
    }

    [Fact]
    public async Task Run_WithoutEncryptionKey_FailsWithoutUploading()
    {
        _settings.Settings.Enabled = true;
        _settings.Settings.Endpoint = "http://192.168.1.50:9000";
        _settings.Settings.BucketName = "backups";
        _settings.EncryptionKey = null;

        var run = await CreateSut().RunAsync();

        Assert.Equal(BackupStatus.Failed, run.Status);
        Assert.Contains("暗号化キー", run.Message);
        Assert.Single(_notifications.Requests);
    }

    [Fact]
    public async Task TestConnection_WithoutBucket_ReportsFailure()
    {
        _settings.Settings.Endpoint = "http://192.168.1.50:9000";
        _settings.Settings.BucketName = null;

        var result = await CreateSut().TestConnectionAsync();

        Assert.False(result.Success);
        Assert.Contains("バケット", result.Message);
    }

    [Fact]
    public async Task TestConnection_WithBlockedEndpoint_IsRejected()
    {
        // SSRF対策: ループバックの保存先は拒否する
        _settings.Settings.Endpoint = "http://127.0.0.1:9000";
        _settings.Settings.BucketName = "backups";

        var result = await CreateSut().TestConnectionAsync();

        Assert.False(result.Success);
    }

    private sealed class StubSourceProvider : IBackupSourceProvider
    {
        public Task<byte[]> CreateSnapshotAsync(CancellationToken ct = default) =>
            Task.FromResult(Encoding.UTF8.GetBytes("{}"));
    }
}

public class FakeBackupSettingsProvider : IBackupSettingsProvider
{
    public BackupSettings Settings { get; } = new();

    public string? AccessKey { get; set; } = "access";

    public string? SecretKey { get; set; } = "secret";

    public string? EncryptionKey { get; set; } = "encryption-key";

    public Task<BackupSettings> GetAsync(CancellationToken ct = default) => Task.FromResult(Settings);

    public Task<string?> GetAccessKeyAsync(CancellationToken ct = default) => Task.FromResult(AccessKey);

    public Task<string?> GetSecretKeyAsync(CancellationToken ct = default) => Task.FromResult(SecretKey);

    public Task<string?> GetEncryptionKeyAsync(CancellationToken ct = default) => Task.FromResult(EncryptionKey);
}

public class FakeBackupRunRepository : IBackupRunRepository
{
    public List<BackupRun> Runs { get; } = [];

    public Task AddAsync(BackupRun run, CancellationToken ct = default)
    {
        run.Id = Runs.Count + 1;
        Runs.Add(run);
        return Task.CompletedTask;
    }

    public Task<List<BackupRun>> GetRecentAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult(Runs.OrderByDescending(r => r.StartedAt).Take(limit).ToList());

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
