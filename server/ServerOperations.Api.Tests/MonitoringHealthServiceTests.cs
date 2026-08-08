using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

/// <summary>
/// 自己監視。収集が止まっていることを検知する。
///
/// これが無いと「障害が無い」と「監視が死んでいる」がどちらも
/// インシデント0件として見え、監視の停止に誰も気づけない。
/// </summary>
public class MonitoringHealthServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeMonitoringTargetRepository _targets = new();
    private readonly FakeMetricSnapshotRepository _snapshots = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    private MonitoringHealthService CreateSut() => new(_targets, _snapshots, _time);

    private MonitoringTarget AddTarget(
        long id = 1,
        bool isEnabled = true,
        int? intervalSeconds = null,
        DateTime? createdAt = null)
    {
        var target = new MonitoringTarget
        {
            Id = id,
            Name = $"target{id}",
            TemplateId = "docker-host",
            IsEnabled = isEnabled,
            CollectionIntervalSeconds = intervalSeconds,
            // 既定では十分に前から存在することにする(登録直後の猶予に引っかからないように)
            CreatedAt = createdAt ?? BaseTime.UtcDateTime.AddDays(-1),
        };
        _targets.Targets.Add(target);
        return target;
    }

    private void AddSnapshot(long targetId, DateTime collectedAt) =>
        _snapshots.Snapshots.Add(new MetricSnapshot
        {
            TargetId = targetId,
            CollectedAt = collectedAt,
            Kind = "docker",
            Status = CollectionStatus.Ok,
        });

    // --- 届いている ---

    [Fact]
    public async Task 直近の収集があれば届いていると判定する()
    {
        AddTarget(intervalSeconds: 60);
        AddSnapshot(1, BaseTime.UtcDateTime.AddSeconds(-30));

        var health = await CreateSut().GetAsync();

        Assert.True(Assert.Single(health).IsHealthy);
    }

    [Fact]
    public async Task 一回分の取りこぼしでは異常としない()
    {
        // 実行の揺れで毎回異常が出ると、本当の停止に気づけなくなる
        AddTarget(intervalSeconds: 60);
        AddSnapshot(1, BaseTime.UtcDateTime.AddSeconds(-90));

        var health = await CreateSut().GetAsync();

        Assert.True(Assert.Single(health).IsHealthy);
    }

    // --- 止まっている ---

    [Fact]
    public async Task 収集が途絶えたら止まっていると判定する()
    {
        AddTarget(intervalSeconds: 60);
        AddSnapshot(1, BaseTime.UtcDateTime.AddMinutes(-30));

        var health = await CreateSut().GetAsync();

        var item = Assert.Single(health);
        Assert.False(item.IsHealthy);
        Assert.Equal(MonitoringReach.Stale, item.Reach);
    }

    [Fact]
    public async Task 最後の収集からの経過を返す()
    {
        AddTarget(intervalSeconds: 60);
        AddSnapshot(1, BaseTime.UtcDateTime.AddMinutes(-30));

        var health = await CreateSut().GetAsync();

        Assert.Equal(1800, Assert.Single(health).StaleForSeconds);
    }

    [Fact]
    public async Task 収集間隔が長い対象では長く待つ()
    {
        // 10分間隔の対象を、1分間隔と同じ基準で「止まった」と言わない
        AddTarget(intervalSeconds: 600);
        AddSnapshot(1, BaseTime.UtcDateTime.AddMinutes(-15));

        var health = await CreateSut().GetAsync();

        Assert.True(Assert.Single(health).IsHealthy);
    }

    [Fact]
    public async Task 収集間隔が未設定なら既定値で判定する()
    {
        AddTarget(intervalSeconds: null);
        AddSnapshot(1, BaseTime.UtcDateTime.AddMinutes(-30));

        var health = await CreateSut().GetAsync();

        Assert.False(Assert.Single(health).IsHealthy);
    }

    [Fact]
    public async Task Workerが止まればすべての対象が止まったと出る()
    {
        // これが「監視そのものが死んでいる」の見え方になる
        AddTarget(id: 1, intervalSeconds: 60);
        AddTarget(id: 2, intervalSeconds: 60);
        AddSnapshot(1, BaseTime.UtcDateTime.AddHours(-2));
        AddSnapshot(2, BaseTime.UtcDateTime.AddHours(-2));

        var health = await CreateSut().GetAsync();

        Assert.Equal(2, health.Count);
        Assert.All(health, h => Assert.False(h.IsHealthy));
    }

    // --- 一度も収集されていない ---

    [Fact]
    public async Task 登録直後は猶予のうち異常としない()
    {
        // 収集が一巡する前に「届いていない」と出すと、登録のたびに警告が出る
        AddTarget(intervalSeconds: 60, createdAt: BaseTime.UtcDateTime.AddSeconds(-30));

        var health = await CreateSut().GetAsync();

        Assert.True(Assert.Single(health).IsHealthy);
    }

    [Fact]
    public async Task 猶予を過ぎても収集が無ければ異常とする()
    {
        AddTarget(intervalSeconds: 60, createdAt: BaseTime.UtcDateTime.AddHours(-2));

        var health = await CreateSut().GetAsync();

        var item = Assert.Single(health);
        Assert.False(item.IsHealthy);
        Assert.Equal(MonitoringReach.NeverCollected, item.Reach);
        Assert.Null(item.LastCollectedAt);
    }

    // --- 対象外 ---

    [Fact]
    public async Task 監視していない対象は判定しない()
    {
        // 止めている対象に収集が無いのは当たり前
        AddTarget(isEnabled: false);

        var health = await CreateSut().GetAsync();

        Assert.Empty(health);
    }

    [Fact]
    public async Task 対象ごとに別々に判定する()
    {
        AddTarget(id: 1, intervalSeconds: 60);
        AddTarget(id: 2, intervalSeconds: 60);
        AddSnapshot(1, BaseTime.UtcDateTime.AddSeconds(-30));
        AddSnapshot(2, BaseTime.UtcDateTime.AddHours(-2));

        var health = await CreateSut().GetAsync();

        Assert.True(health.Single(h => h.TargetId == 1).IsHealthy);
        Assert.False(health.Single(h => h.TargetId == 2).IsHealthy);
    }

    [Fact]
    public async Task 時計のずれで経過が負になっても0として扱う()
    {
        AddTarget(intervalSeconds: 60);
        AddSnapshot(1, BaseTime.UtcDateTime.AddMinutes(5));

        var health = await CreateSut().GetAsync();

        Assert.Equal(0, Assert.Single(health).StaleForSeconds);
    }
}
