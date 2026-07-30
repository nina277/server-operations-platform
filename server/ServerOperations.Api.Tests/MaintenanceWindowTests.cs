using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

/// <summary>
/// メンテナンス期間。計画停止中に通知と自動復旧を止める。
/// 検知そのものは止めないため、記録は残り続ける。
/// </summary>
public class MaintenanceWindowTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeMaintenanceWindowRepository _windows = new();
    private readonly FakeMonitoringTargetRepository _targets = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    private MaintenanceWindowService CreateSut() =>
        new(_windows, _targets, _audit, _currentUser, _time);

    private MaintenanceService CreateChecker() => new(_windows, _time);

    private static CreateMaintenanceWindowRequest Request(
        long? targetId = null,
        int startsInHours = 0,
        int durationHours = 2,
        bool suppressNotifications = true,
        bool suppressAutoRecovery = true) => new()
    {
        TargetId = targetId,
        Reason = "ホストのカーネル更新",
        StartsAt = BaseTime.UtcDateTime.AddHours(startsInHours),
        EndsAt = BaseTime.UtcDateTime.AddHours(startsInHours + durationHours),
        SuppressNotifications = suppressNotifications,
        SuppressAutoRecovery = suppressAutoRecovery,
    };

    // --- 登録 ---

    [Fact]
    public async Task 期間を登録できる()
    {
        var created = await CreateSut().CreateAsync(Request());

        Assert.True(created.IsActive);
        Assert.True(created.SuppressNotifications);
        Assert.True(created.SuppressAutoRecovery);
    }

    [Fact]
    public async Task 登録を監査に残す()
    {
        await CreateSut().CreateAsync(Request());

        Assert.Contains(_audit.Entries, e => e.Action == "maintenance.create");
    }

    [Fact]
    public async Task 終了が開始より前なら拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().CreateAsync(Request() with
            {
                EndsAt = BaseTime.UtcDateTime.AddHours(-1),
            }));

        Assert.Equal("invalid_maintenance_range", ex.Code);
    }

    [Fact]
    public async Task すでに終わった期間は登録できない()
    {
        // 過去の抑止を後から入れられると、通知が飛ばなかった説明を作れてしまう
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().CreateAsync(Request(startsInHours: -5, durationHours: 1)));

        Assert.Equal("maintenance_in_past", ex.Code);
    }

    [Fact]
    public async Task 何も止めない期間は登録できない()
    {
        // 登録しても効かない設定を作らせない
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().CreateAsync(
                Request(suppressNotifications: false, suppressAutoRecovery: false)));

        Assert.Equal("maintenance_no_effect", ex.Code);
    }

    [Fact]
    public async Task 長すぎる期間は登録できない()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().CreateAsync(Request(durationHours: 24 * 40)));

        Assert.Equal("maintenance_too_long", ex.Code);
    }

    [Fact]
    public async Task 存在しない対象は指定できない()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().CreateAsync(Request(targetId: 999)));

        Assert.Equal("target_not_found", ex.Code);
    }

    // --- 取り消し ---

    [Fact]
    public async Task 期間を取り消せる()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());

        var cancelled = await sut.CancelAsync(created.Id);

        Assert.NotNull(cancelled.CancelledAt);
        Assert.False(cancelled.IsActive);
    }

    [Fact]
    public async Task 取り消しを監査に残す()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());

        await sut.CancelAsync(created.Id);

        Assert.Contains(_audit.Entries, e => e.Action == "maintenance.cancel");
    }

    [Fact]
    public async Task 二重の取り消しは拒否する()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());
        await sut.CancelAsync(created.Id);

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.CancelAsync(created.Id));

        Assert.Equal("maintenance_already_cancelled", ex.Code);
    }

    // --- 抑止の判定 ---

    [Fact]
    public async Task 期間中は抑止する()
    {
        await CreateSut().CreateAsync(Request());

        var state = await CreateChecker().GetStateAsync(targetId: 1);

        Assert.True(state.SuppressNotifications);
        Assert.True(state.SuppressAutoRecovery);
        Assert.Equal("ホストのカーネル更新", state.Reason);
    }

    [Fact]
    public async Task 期間の前は抑止しない()
    {
        await CreateSut().CreateAsync(Request(startsInHours: 3));

        var state = await CreateChecker().GetStateAsync(targetId: 1);

        Assert.False(state.SuppressNotifications);
        Assert.False(state.SuppressAutoRecovery);
    }

    [Fact]
    public async Task 期間が終われば抑止しない()
    {
        await CreateSut().CreateAsync(Request(durationHours: 2));

        _time.Advance(TimeSpan.FromHours(3));
        var state = await CreateChecker().GetStateAsync(targetId: 1);

        Assert.False(state.SuppressNotifications);
    }

    [Fact]
    public async Task 取り消した期間は抑止しない()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());
        await sut.CancelAsync(created.Id);

        var state = await CreateChecker().GetStateAsync(targetId: 1);

        Assert.False(state.SuppressNotifications);
    }

    [Fact]
    public async Task 対象を指定した期間は他の対象に効かない()
    {
        _targets.Targets.Add(new MonitoringTarget
        {
            Id = 1, Name = "docker1", TemplateId = "docker-host",
        });
        await CreateSut().CreateAsync(Request(targetId: 1));

        var other = await CreateChecker().GetStateAsync(targetId: 2);

        Assert.False(other.SuppressNotifications);
    }

    [Fact]
    public async Task 対象を指定しない期間はすべての対象に効く()
    {
        await CreateSut().CreateAsync(Request(targetId: null));

        var state = await CreateChecker().GetStateAsync(targetId: 42);

        Assert.True(state.SuppressNotifications);
    }

    [Fact]
    public async Task 通知だけを止める期間では自動復旧は止めない()
    {
        await CreateSut().CreateAsync(
            Request(suppressNotifications: true, suppressAutoRecovery: false));

        var state = await CreateChecker().GetStateAsync(targetId: 1);

        Assert.True(state.SuppressNotifications);
        Assert.False(state.SuppressAutoRecovery);
    }

    [Fact]
    public async Task 期間が重なるときはどれか一つでも止めていれば止める()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Request(suppressNotifications: true, suppressAutoRecovery: false));
        await sut.CreateAsync(Request(suppressNotifications: false, suppressAutoRecovery: true));

        var state = await CreateChecker().GetStateAsync(targetId: 1);

        Assert.True(state.SuppressNotifications);
        Assert.True(state.SuppressAutoRecovery);
    }

    // --- 一覧 ---

    [Fact]
    public async Task 終わった期間は一覧に出さない()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Request(durationHours: 1));
        _time.Advance(TimeSpan.FromHours(2));

        var upcoming = await sut.GetUpcomingAsync();

        Assert.Empty(upcoming);
    }

    [Fact]
    public async Task 予定の期間は一覧に出す()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Request(startsInHours: 24));

        var upcoming = await sut.GetUpcomingAsync();

        var window = Assert.Single(upcoming);
        Assert.False(window.IsActive);
    }
}
