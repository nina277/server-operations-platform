using ServerOperations.Api.Services.Implementations;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Tests;

/// <summary>
/// 運用実績の集計。docs/verification.md の成功基準を画面から測れるようにするもの。
/// 集計の取り違えは論文の主張を直接誤らせるため、境界と分母の扱いを確かめる。
/// </summary>
public class OperationsInsightsServiceTests
{
    private static readonly DateTime From = new(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime To = new(2026, 7, 31, 0, 0, 0, DateTimeKind.Utc);

    private readonly StubInsightsRepository _repo = new();

    private OperationsInsightsService CreateSut() => new(_repo);

    // --- 期間の指定 ---

    [Fact]
    public async Task 開始が終了以降なら拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().GetAsync(To, From));

        Assert.Equal("invalid_range", ex.Code);
    }

    [Fact]
    public async Task 長すぎる期間は拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().GetAsync(From, From.AddDays(OperationsInsightsService.MaxRangeDays + 1)));

        Assert.Equal("range_too_wide", ex.Code);
    }

    // --- 検知から通知まで(成功基準#2) ---

    [Fact]
    public async Task 検知から通知までの分布を返す()
    {
        _repo.DetectionSeconds = [10, 20, 30, 40, 50];

        var result = await CreateSut().GetAsync(From, To);

        Assert.Equal(5, result.DetectionToNotification.Count);
        Assert.Equal(30, result.DetectionToNotification.AverageSeconds);
        Assert.Equal(30, result.DetectionToNotification.MedianSeconds);
        Assert.Equal(50, result.DetectionToNotification.MaxSeconds);
    }

    [Fact]
    public async Task 基準の秒数以内に通知できた割合を返す()
    {
        // 300秒が基準。3件中2件が基準内。
        _repo.DetectionSeconds = [100, 200, 400];

        var result = await CreateSut().GetAsync(From, To);

        Assert.Equal(300, result.NotificationTargetSeconds);
        Assert.Equal(2.0 / 3, result.NotifiedWithinTargetRatio);
    }

    [Fact]
    public async Task ちょうど基準の秒数は基準内として数える()
    {
        // 「5分以内」の境界。300秒を超過扱いにすると基準の解釈がずれる。
        _repo.DetectionSeconds = [300];

        var result = await CreateSut().GetAsync(From, To);

        Assert.Equal(1.0, result.NotifiedWithinTargetRatio);
    }

    [Fact]
    public async Task 通知が一件も無ければ割合は返さない()
    {
        // 0件を0%として出すと「一度も間に合っていない」と読めてしまう
        _repo.DetectionSeconds = [];

        var result = await CreateSut().GetAsync(From, To);

        Assert.Null(result.NotifiedWithinTargetRatio);
        Assert.Equal(0, result.DetectionToNotification.Count);
        Assert.Null(result.DetectionToNotification.AverageSeconds);
    }

    // --- 自動復旧の成功率 ---

    [Fact]
    public async Task 自動復旧の成功率は成功と失敗の合計に対して測る()
    {
        _repo.AutoByStatus = new()
        {
            [RecoveryActionStatus.Succeeded] = 3,
            [RecoveryActionStatus.Failed] = 1,
        };

        var result = await CreateSut().GetAsync(From, To);

        Assert.Equal(0.75, result.AutoRecoverySuccessRatio);
    }

    [Fact]
    public async Task 安全機構が止めた件数は成功率の分母に入れない()
    {
        // Blockedを分母に入れると、安全に止めるほど成功率が下がり、
        // 安全機構が効いていることが悪い結果として出てしまう。
        _repo.AutoByStatus = new()
        {
            [RecoveryActionStatus.Succeeded] = 1,
            [RecoveryActionStatus.Blocked] = 9,
        };

        var result = await CreateSut().GetAsync(From, To);

        Assert.Equal(1.0, result.AutoRecoverySuccessRatio);
    }

    [Fact]
    public async Task 自動実行が一件も無ければ成功率は返さない()
    {
        _repo.AutoByStatus = [];

        var result = await CreateSut().GetAsync(From, To);

        Assert.Null(result.AutoRecoverySuccessRatio);
    }

    // --- 安全機構が止めた理由 ---

    [Fact]
    public async Task 自動実行を止めた理由の内訳を返す()
    {
        // 何回・何を止めたかは、安全機構が効いていることの直接の証拠になる
        _repo.BlockedReasons = new() { ["cooldown"] = 4, ["circuit_open"] = 2 };

        var result = await CreateSut().GetAsync(From, To);

        Assert.Equal(4, result.BlockedReasons["cooldown"]);
        Assert.Equal(2, result.BlockedReasons["circuit_open"]);
    }

    // --- インシデント ---

    [Fact]
    public async Task 検知件数と解決件数を返す()
    {
        _repo.Detected = 10;
        _repo.Resolved = 7;

        var result = await CreateSut().GetAsync(From, To);

        Assert.Equal(10, result.IncidentsDetected);
        Assert.Equal(7, result.IncidentsResolved);
    }

    [Fact]
    public async Task 重大度ごとの件数を文字列のキーで返す()
    {
        _repo.BySeverity = new() { [IncidentSeverity.Critical] = 2, [IncidentSeverity.Low] = 5 };

        var result = await CreateSut().GetAsync(From, To);

        Assert.Equal(2, result.IncidentsBySeverity["Critical"]);
        Assert.Equal(5, result.IncidentsBySeverity["Low"]);
    }

    /// <summary>集計の入力だけを差し替える。DBは使わない。</summary>
    private class StubInsightsRepository : IOperationsInsightsRepository
    {
        public List<double> DetectionSeconds { get; set; } = [];

        public List<double> RecoverySeconds { get; set; } = [];

        public Dictionary<RecoveryActionStatus, int> AutoByStatus { get; set; } = [];

        public Dictionary<string, int> BlockedReasons { get; set; } = [];

        public Dictionary<IncidentSeverity, int> BySeverity { get; set; } = [];

        public int Detected { get; set; }

        public int Resolved { get; set; }

        public Task<List<double>> GetDetectionToNotificationSecondsAsync(
            OperationsInsightsRange range, CancellationToken ct = default) =>
            Task.FromResult(DetectionSeconds);

        public Task<List<double>> GetRecoveryDurationSecondsAsync(
            OperationsInsightsRange range, bool automaticOnly, CancellationToken ct = default) =>
            Task.FromResult(RecoverySeconds);

        public Task<Dictionary<RecoveryActionStatus, int>> CountRecoveryByStatusAsync(
            OperationsInsightsRange range, bool automaticOnly, CancellationToken ct = default) =>
            Task.FromResult(automaticOnly ? AutoByStatus : []);

        public Task<Dictionary<string, int>> CountBlockedReasonsAsync(
            OperationsInsightsRange range, CancellationToken ct = default) =>
            Task.FromResult(BlockedReasons);

        public Task<Dictionary<IncidentSeverity, int>> CountIncidentsBySeverityAsync(
            OperationsInsightsRange range, CancellationToken ct = default) =>
            Task.FromResult(BySeverity);

        public Task<(int Detected, int Resolved)> CountIncidentOutcomesAsync(
            OperationsInsightsRange range, CancellationToken ct = default) =>
            Task.FromResult((Detected, Resolved));

        public Task<RecurrenceSummary> GetRecurrenceAsync(
            long targetId, string signature, long excludeIncidentId, CancellationToken ct = default) =>
            Task.FromResult(new RecurrenceSummary { TotalCount = 0, ResolvedCount = 0 });
    }
}
