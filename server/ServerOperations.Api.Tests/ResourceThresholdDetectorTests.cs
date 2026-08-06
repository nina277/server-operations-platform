using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

/// <summary>
/// しきい値ルールを「検知」に使う判定。
///
/// これまでルールは、別の経路で作られたインシデントを説明するためだけに使われていた。
/// そのため使用率のしきい値ルールは、どれだけ逼迫しても自分では何も起こせなかった。
/// </summary>
public class ResourceThresholdDetectorTests
{
    private readonly FakeDiagnosticRuleRepository _rules = new();

    private ResourceThresholdDetector CreateSut() => new(_rules, new RuleEngine());

    private void AddRule(
        string name,
        string conditionJson,
        int priority = 10,
        bool isEnabled = true,
        IncidentSeverity severity = IncidentSeverity.Medium,
        DiagnosticRuleType ruleType = DiagnosticRuleType.Threshold)
    {
        _rules.Rules.Add(new DiagnosticRule
        {
            Id = _rules.Rules.Count + 1,
            Name = name,
            Classification = "ResourcePressure",
            RuleType = ruleType,
            ConditionJson = conditionJson,
            Severity = severity,
            Priority = priority,
            IsEnabled = isEnabled,
            RationaleTemplate = "{field} が {value} です(判定条件: {expected})。",
        });
    }

    private static ContainerResourceSample Sample(
        string name = "web", double? cpu = null, double? memory = null) =>
        new(name, new ContainerStats(cpu, memory, MemoryUsageBytes: null, MemoryLimitBytes: null));

    [Fact]
    public async Task しきい値を超えたら検知する()
    {
        AddRule("メモリ逼迫", """{"field":"memoryUsagePercent","operator":">=","value":90}""");

        var alerts = await CreateSut().DetectAsync([Sample(memory: 95)]);

        var alert = Assert.Single(alerts);
        Assert.Equal("web", alert.ContainerName);
        Assert.Equal("メモリ逼迫", alert.Rule.Name);
        Assert.Contains("95", alert.Rationale);
    }

    [Fact]
    public async Task しきい値を超えていなければ検知しない()
    {
        AddRule("メモリ逼迫", """{"field":"memoryUsagePercent","operator":">=","value":90}""");

        var alerts = await CreateSut().DetectAsync([Sample(memory: 40)]);

        Assert.Empty(alerts);
    }

    [Fact]
    public async Task 値が取れていないコンテナは判定しない()
    {
        // 「値が無い」を「しきい値を下回っている」と読み替えると逼迫を見逃す。
        // かといって発火させるのも誤りなので、判定自体を行わない。
        AddRule("メモリ逼迫", """{"field":"memoryUsagePercent","operator":">=","value":90}""");
        AddRule("メモリ余裕", """{"field":"memoryUsagePercent","operator":"<","value":90}""", priority: 20);

        var alerts = await CreateSut().DetectAsync([Sample(memory: null)]);

        Assert.Empty(alerts);
    }

    [Fact]
    public async Task CPUだけ取れていればCPUのルールは判定する()
    {
        // 片方が取れなかっただけで、取れたほうまで見ないのは行き過ぎ
        AddRule("CPU逼迫", """{"field":"cpuUsagePercent","operator":">=","value":80}""");

        var alerts = await CreateSut().DetectAsync([Sample(cpu: 95, memory: null)]);

        Assert.Single(alerts);
    }

    [Fact]
    public async Task コンテナ名を条件にしたルールは発火しない()
    {
        // 使用率の収集は毎回すべての稼働コンテナについて行われる。
        // ここでコンテナ名を判定に渡すと、名前を条件にした状態ルールが
        // 毎回一致し、正常なコンテナのインシデントを作り続ける。
        AddRule(
            "web という名前",
            """{"field":"containerName","equalsAny":["web"]}""",
            ruleType: DiagnosticRuleType.State);

        var alerts = await CreateSut().DetectAsync([Sample(name: "web", memory: 10)]);

        Assert.Empty(alerts);
    }

    [Fact]
    public async Task ログを条件にしたルールも発火しない()
    {
        // 使用率の収集ではログを取らない。空のログに当たるパターンを書かれても影響させない
        AddRule(
            "何にでも当たる",
            """{"field":"logExcerpt","pattern":".*"}""",
            ruleType: DiagnosticRuleType.Regex);

        var alerts = await CreateSut().DetectAsync([Sample(memory: 10)]);

        Assert.Empty(alerts);
    }

    [Fact]
    public async Task 無効にしたルールは使わない()
    {
        AddRule(
            "メモリ逼迫",
            """{"field":"memoryUsagePercent","operator":">=","value":90}""",
            isEnabled: false);

        var alerts = await CreateSut().DetectAsync([Sample(memory: 95)]);

        Assert.Empty(alerts);
    }

    [Fact]
    public async Task 複数当たったら優先度の高いものを採る()
    {
        AddRule("重い", """{"field":"memoryUsagePercent","operator":">=","value":95}""", priority: 1);
        AddRule("軽い", """{"field":"memoryUsagePercent","operator":">=","value":90}""", priority: 50);

        var alerts = await CreateSut().DetectAsync([Sample(memory: 99)]);

        var alert = Assert.Single(alerts);
        Assert.Equal("重い", alert.Rule.Name);
    }

    [Fact]
    public async Task コンテナごとに1件ずつ返す()
    {
        AddRule("メモリ逼迫", """{"field":"memoryUsagePercent","operator":">=","value":90}""");

        var alerts = await CreateSut().DetectAsync(
            [Sample("web", memory: 95), Sample("db", memory: 99), Sample("cache", memory: 10)]);

        Assert.Equal(2, alerts.Count);
        Assert.Equal(["web", "db"], alerts.Select(a => a.ContainerName));
    }

    [Fact]
    public async Task 収集が無ければルールを取りに行かない()
    {
        AddRule("メモリ逼迫", """{"field":"memoryUsagePercent","operator":">=","value":90}""");

        var alerts = await CreateSut().DetectAsync([]);

        Assert.Empty(alerts);
    }
}
