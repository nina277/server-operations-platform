using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Services.Backup;

namespace ServerOperations.Api.Tests;

/// <summary>
/// バックアップからの復元。
///
/// **復元は破壊的で、それ自体が乗っ取りの経路になりうる。**
/// 「何を戻さないか」「何を消さないか」がこの機能の本体なので、そこを固定する。
/// </summary>
public class BackupRestoreServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeBackupSettingsProvider _settings = new();
    private readonly FakeBackupObjectStore _store = new();
    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"restore-{Guid.NewGuid()}")
            .Options);

    private BackupRestoreService CreateSut() => new(
        _settings, _store, _db, new TestTimeProvider(Now),
        NullLogger<BackupRestoreService>.Instance);

    /// <summary>スナップショットを暗号化して保存先へ置く。</summary>
    private void PutBackup(object snapshot, string key = "server-operations/backup-1.bin")
    {
        var plaintext = Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(snapshot, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        _store.Objects[key] = BackupService.Encrypt(plaintext, _settings.EncryptionKey!);
        _store.Generations.Add(new BackupGeneration(key, Now.UtcDateTime, plaintext.Length));
    }

    private static object Snapshot(
        object[]? users = null, object[]? targets = null, object[]? profiles = null,
        object[]? rules = null, object[]? cidrs = null, object[]? settings = null) => new
        {
            version = 1,
            createdAt = Now.UtcDateTime,
            users = users ?? [],
            settings = settings ?? [],
            trustedNetworkCidrs = cidrs ?? [],
            monitoringTargets = targets ?? [],
            targetProfiles = profiles ?? [],
            diagnosticRules = rules ?? [],
        };

    // --- 戻さないもの ---------------------------------------------------

    [Fact]
    public async Task 利用者は復元しない()
    {
        // バックアップにパスワードハッシュが無いため復元しても誰もログインできない。
        // さらに、降格した利用者を古いバックアップで戻せると権限昇格の手段になる
        PutBackup(Snapshot(users:
        [
            new { Id = 1L, Username = "admin", Role = "OperatorAdmin", IsActive = true, CreatedAt = Now.UtcDateTime },
        ]));

        var plan = await CreateSut().RestoreAsync("server-operations/backup-1.bin", userId: 1);

        Assert.Empty(await _db.Users.ToListAsync());
        Assert.Contains(plan.Notes, n => n.Contains("利用者") && n.Contains("復元しません"));
    }

    [Fact]
    public async Task 降格した利用者の役割を戻さない()
    {
        // 権限昇格の経路にしない
        _db.Users.Add(new User
        {
            Id = 1, Username = "someone", PasswordHash = "hash",
            Role = UserRole.Viewer, IsActive = false,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await _db.SaveChangesAsync();

        PutBackup(Snapshot(users:
        [
            new { Id = 1L, Username = "someone", Role = "OperatorAdmin", IsActive = true, CreatedAt = Now.UtcDateTime },
        ]));

        await CreateSut().RestoreAsync("server-operations/backup-1.bin", userId: 1);

        var user = await _db.Users.SingleAsync();
        Assert.Equal(UserRole.Viewer, user.Role);
        Assert.False(user.IsActive);
    }

    [Fact]
    public async Task バックアップに無い監視対象は消さない()
    {
        // 「置き換える」を素直に実装すると、バックアップ以後に作った対象が消える
        _db.MonitoringTargets.Add(new MonitoringTarget
        {
            Name = "後から作った対象", TemplateId = "docker-host",
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await _db.SaveChangesAsync();

        PutBackup(Snapshot(targets: []));

        var plan = await CreateSut().RestoreAsync("server-operations/backup-1.bin", userId: 1);

        Assert.Single(await _db.MonitoringTargets.ToListAsync());
        var item = plan.Items.Single(i => i.Category == "監視対象");
        Assert.Equal(1, item.NotInBackup);
    }

    // --- 自動復旧が広がらないこと ---------------------------------------

    [Fact]
    public async Task 復元で作られた対象は自動復旧が無効で許可リストが空()
    {
        // 自動復旧の有無と許可コンテナはバックアップに含まれない。
        // **復元が自動実行の範囲を広げる経路にならないこと**を固定する
        PutBackup(Snapshot(targets:
        [
            new { Id = 5L, Name = "戻す対象", TemplateId = "docker-host", Description = (string?)null, IsEnabled = true, CreatedAt = Now.UtcDateTime },
        ]));

        await CreateSut().RestoreAsync("server-operations/backup-1.bin", userId: 1);

        var target = await _db.MonitoringTargets.SingleAsync();
        Assert.False(target.AutoRecoveryEnabled);
        Assert.Equal("[]", target.AllowedContainersJson);
    }

    [Fact]
    public async Task 既存対象の自動復旧設定を復元で書き換えない()
    {
        _db.MonitoringTargets.Add(new MonitoringTarget
        {
            Name = "既存", TemplateId = "docker-host", Description = "変更前",
            AutoRecoveryEnabled = true, AllowedContainersJson = """["web"]""",
            CollectionIntervalSeconds = 30,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
        });
        await _db.SaveChangesAsync();

        PutBackup(Snapshot(targets:
        [
            new { Id = 1L, Name = "既存", TemplateId = "docker-host", Description = "変更後", IsEnabled = true, CreatedAt = Now.UtcDateTime },
        ]));

        await CreateSut().RestoreAsync("server-operations/backup-1.bin", userId: 1);

        var target = await _db.MonitoringTargets.SingleAsync();
        Assert.Equal("変更後", target.Description);        // バックアップにある項目は戻る
        Assert.True(target.AutoRecoveryEnabled);           // 無い項目には触れない
        Assert.Equal("""["web"]""", target.AllowedContainersJson);
        Assert.Equal(30, target.CollectionIntervalSeconds);
    }

    // --- 下見 -----------------------------------------------------------

    [Fact]
    public async Task 下見では何も変更しない()
    {
        PutBackup(Snapshot(
            targets: [new { Id = 1L, Name = "新規", TemplateId = "docker-host", Description = (string?)null, IsEnabled = true, CreatedAt = Now.UtcDateTime }],
            rules: [new { Name = "規則", Classification = "X", RuleType = "Regex", ConditionJson = "{}", Severity = "High", RecommendedActionId = (string?)null, Priority = 1, RationaleTemplate = "t", IsEnabled = true }]));

        var plan = await CreateSut().PreviewAsync("server-operations/backup-1.bin");

        Assert.False(plan.Applied);
        Assert.Empty(await _db.MonitoringTargets.ToListAsync());
        Assert.Empty(await _db.DiagnosticRules.ToListAsync());

        // 何が起きるかは示す
        Assert.Equal(1, plan.Items.Single(i => i.Category == "監視対象").Added);
        Assert.Equal(1, plan.Items.Single(i => i.Category == "診断ルール").Added);
    }

    // --- 復元されるもの -------------------------------------------------

    [Fact]
    public async Task 監視対象とルールと許可ネットワークは復元される()
    {
        PutBackup(Snapshot(
            targets: [new { Id = 7L, Name = "戻る対象", TemplateId = "web-site", Description = "説明", IsEnabled = true, CreatedAt = Now.UtcDateTime }],
            profiles: [new { TargetId = 7L, SettingsJson = """{"url":"http://example.test/health"}""", UpdatedAt = Now.UtcDateTime }],
            rules: [new { Name = "戻る規則", Classification = "DiskPressure", RuleType = "Regex", ConditionJson = """{"field":"logExcerpt","pattern":"x"}""", Severity = "High", RecommendedActionId = (string?)null, Priority = 5, RationaleTemplate = "t", IsEnabled = true }],
            cidrs: [new { Cidr = "192.168.1.0/24", Description = "自宅", CreatedAt = Now.UtcDateTime }]));

        await CreateSut().RestoreAsync("server-operations/backup-1.bin", userId: 1);

        var target = await _db.MonitoringTargets.Include(t => t.Profile).SingleAsync();
        Assert.Equal("戻る対象", target.Name);
        Assert.Equal("web-site", target.TemplateId);
        Assert.Contains("example.test", target.Profile!.SettingsJson);

        Assert.Equal("戻る規則", (await _db.DiagnosticRules.SingleAsync()).Name);
        Assert.Equal("192.168.1.0/24", (await _db.TrustedNetworkCidrs.SingleAsync()).Cidr);
    }

    [Fact]
    public async Task 同じ内容を二度復元しても増えない()
    {
        PutBackup(Snapshot(
            targets: [new { Id = 1L, Name = "対象", TemplateId = "docker-host", Description = (string?)null, IsEnabled = true, CreatedAt = Now.UtcDateTime }],
            cidrs: [new { Cidr = "10.0.0.0/8", Description = (string?)null, CreatedAt = Now.UtcDateTime }]));

        await CreateSut().RestoreAsync("server-operations/backup-1.bin", userId: 1);
        var plan = await CreateSut().RestoreAsync("server-operations/backup-1.bin", userId: 1);

        Assert.Single(await _db.MonitoringTargets.ToListAsync());
        Assert.Single(await _db.TrustedNetworkCidrs.ToListAsync());
        Assert.Equal(0, plan.Items.Single(i => i.Category == "監視対象").Added);
    }

    // --- 失敗の扱い -----------------------------------------------------

    [Fact]
    public async Task 保存先に無いキーは受け付けない()
    {
        // 任意のキーを取りに行かせない
        PutBackup(Snapshot());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().PreviewAsync("server-operations/存在しない.bin"));

        Assert.Contains("保存先にありません", error.Message);
    }

    [Fact]
    public async Task 暗号化キーが違うと復元できない()
    {
        PutBackup(Snapshot(targets:
        [
            new { Id = 1L, Name = "対象", TemplateId = "docker-host", Description = (string?)null, IsEnabled = true, CreatedAt = Now.UtcDateTime },
        ]));
        _settings.EncryptionKey = "別の鍵";

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().RestoreAsync("server-operations/backup-1.bin", userId: 1));

        Assert.Contains("復号できません", error.Message);
        Assert.Empty(await _db.MonitoringTargets.ToListAsync());
    }

    [Fact]
    public async Task 暗号化キーが未設定なら復元できない()
    {
        PutBackup(Snapshot());
        _settings.EncryptionKey = null;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().PreviewAsync("server-operations/backup-1.bin"));
    }
}

/// <summary>保存先の代わり。復元の判断だけを試験するために置く。</summary>
public class FakeBackupObjectStore : IBackupObjectStore
{
    public Dictionary<string, byte[]> Objects { get; } = [];

    public List<BackupGeneration> Generations { get; } = [];

    public Task<List<BackupGeneration>> ListAsync(CancellationToken ct = default) =>
        Task.FromResult(Generations);

    public Task<byte[]> GetAsync(string objectKey, CancellationToken ct = default) =>
        Objects.TryGetValue(objectKey, out var blob)
            ? Task.FromResult(blob)
            : throw new InvalidOperationException("指定されたバックアップが保存先にありません。");
}
