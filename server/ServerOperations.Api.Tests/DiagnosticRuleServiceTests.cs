using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class DiagnosticRuleServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeDiagnosticRuleRepository _rules = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    private DiagnosticRuleService CreateSut() =>
        new(_rules, new RecoveryActionCatalog(), _audit, _currentUser, _time);

    private static SaveDiagnosticRuleRequest Request(
        string name = "コンテナ停止",
        string ruleType = "State",
        string conditionJson = """{"field":"containerState","equalsAny":["exited"]}""",
        string severity = "High",
        string? recommendedActionId = RecoveryActionCatalog.RestartAllowedContainer,
        int priority = 10,
        bool isEnabled = true) => new()
    {
        Name = name,
        Classification = "ContainerStopped",
        RuleType = ruleType,
        ConditionJson = conditionJson,
        Severity = severity,
        RecommendedActionId = recommendedActionId,
        Priority = priority,
        RationaleTemplate = "コンテナ状態({field})が {value} です。",
        IsEnabled = isEnabled,
    };

    // --- 作成 ---

    [Fact]
    public async Task 正しい内容ならルールを作れる()
    {
        var rule = await CreateSut().CreateAsync(Request());

        Assert.Equal("コンテナ停止", rule.Name);
        Assert.Equal("State", rule.RuleType);
        Assert.Equal("High", rule.Severity);
        Assert.Equal(RecoveryActionCatalog.RestartAllowedContainer, rule.RecommendedActionId);
        Assert.Single(_rules.Rules);
    }

    [Fact]
    public async Task 作成を監査に残す()
    {
        await CreateSut().CreateAsync(Request());

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("diagnostic_rule.create", entry.Action);
        Assert.Equal("DiagnosticRule", entry.TargetType);
        Assert.Equal(AuditResult.Success, entry.Result);
        Assert.Equal(1, entry.ActorUserId);
        Assert.Contains("コンテナ停止", entry.Details);
    }

    [Fact]
    public async Task 同じ名前のルールは作れない()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Request(name: "重複する名前"));

        var ex = await Assert.ThrowsAsync<AppException>(
            () => sut.CreateAsync(Request(name: "重複する名前")));

        Assert.Equal("duplicate_rule_name", ex.Code);
        Assert.Single(_rules.Rules);
    }

    // --- 条件の検証 ---

    [Fact]
    public async Task 既知でない項目を参照する条件は拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().CreateAsync(
            Request(conditionJson: """{"field":"apiToken","equalsAny":["x"]}""")));

        Assert.Equal("invalid_condition", ex.Code);
        Assert.Empty(_rules.Rules);
    }

    [Fact]
    public async Task 評価に時間がかかりすぎる正規表現は拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().CreateAsync(
            Request(
                ruleType: "Regex",
                conditionJson: """{"field":"logExcerpt","pattern":"(a+)+$"}""")));

        Assert.Equal("invalid_condition", ex.Code);
        Assert.Empty(_rules.Rules);
    }

    [Fact]
    public async Task 壊れた条件は拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().CreateAsync(
            Request(conditionJson: "これは条件です")));

        Assert.Equal("invalid_condition", ex.Code);
    }

    [Fact]
    public async Task 未知のルール種別は拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().CreateAsync(
            Request(ruleType: "Script")));

        Assert.Equal("invalid_rule_type", ex.Code);
    }

    [Fact]
    public async Task 未知の深刻度は拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().CreateAsync(
            Request(severity: "Catastrophic")));

        Assert.Equal("invalid_severity", ex.Code);
    }

    // --- 推奨アクションの検証 ---

    [Theory]
    [InlineData("rm -rf /")]
    [InlineData("EXEC_ARBITRARY_COMMAND")]
    [InlineData("DELETE_ALL_CONTAINERS")]
    [InlineData("RESTART_ALLOWED_CONTAINER; rm -rf /")]
    public async Task 許可リストに無い推奨アクションは拒否する(string actionId)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().CreateAsync(
            Request(recommendedActionId: actionId)));

        Assert.Equal("invalid_recommended_action", ex.Code);
        Assert.Empty(_rules.Rules);
    }

    [Fact]
    public async Task 推奨アクションを指定しないルールは作れる()
    {
        var rule = await CreateSut().CreateAsync(Request(recommendedActionId: null));

        Assert.Null(rule.RecommendedActionId);
    }

    [Fact]
    public void 推奨アクションの候補は復旧の許可リストと一致する()
    {
        var options = CreateSut().GetEditorOptions();
        var catalog = new RecoveryActionCatalog().GetAll().Select(d => d.ActionId).ToList();

        Assert.Equal(catalog, options.RecommendedActionIds);
    }

    [Fact]
    public void 条件で参照できる項目は収集値に限る()
    {
        var options = CreateSut().GetEditorOptions();

        Assert.Contains("containerState", options.Fields);
        Assert.Contains("logExcerpt", options.Fields);
        Assert.DoesNotContain("password", options.Fields);
    }

    // --- 更新 ---

    [Fact]
    public async Task ルールを更新できる()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());

        var updated = await sut.UpdateAsync(created.Id, Request(
            name: "コンテナ停止(改)", severity: "Critical", priority: 5));

        Assert.Equal("コンテナ停止(改)", updated.Name);
        Assert.Equal("Critical", updated.Severity);
        Assert.Equal(5, updated.Priority);
        Assert.Single(_rules.Rules);
    }

    [Fact]
    public async Task 更新では変更前後を監査に残す()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request(severity: "High"));
        _audit.Entries.Clear();

        await sut.UpdateAsync(created.Id, Request(severity: "Low"));

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("diagnostic_rule.update", entry.Action);
        Assert.Contains("変更前", entry.Details);
        Assert.Contains("変更後", entry.Details);
        Assert.Contains("High", entry.Details);
        Assert.Contains("Low", entry.Details);
    }

    [Fact]
    public async Task 更新でも許可リスト外の推奨アクションは拒否する()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.UpdateAsync(
            created.Id, Request(recommendedActionId: "EXEC_ARBITRARY_COMMAND")));

        Assert.Equal("invalid_recommended_action", ex.Code);
        // 変更されていないこと
        Assert.Equal(RecoveryActionCatalog.RestartAllowedContainer, _rules.Rules[0].RecommendedActionId);
    }

    [Fact]
    public async Task 更新でも壊れた条件は拒否し元の条件を保つ()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());
        var originalCondition = _rules.Rules[0].ConditionJson;

        await Assert.ThrowsAsync<AppException>(() => sut.UpdateAsync(
            created.Id, Request(conditionJson: """{"field":"unknown","equalsAny":["x"]}""")));

        Assert.Equal(originalCondition, _rules.Rules[0].ConditionJson);
    }

    [Fact]
    public async Task 自分自身の名前は重複扱いにしない()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request(name: "そのまま"));

        var updated = await sut.UpdateAsync(created.Id, Request(name: "そのまま", priority: 20));

        Assert.Equal("そのまま", updated.Name);
        Assert.Equal(20, updated.Priority);
    }

    [Fact]
    public async Task 存在しないルールの更新は404()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().UpdateAsync(999, Request()));

        Assert.Equal("diagnostic_rule_not_found", ex.Code);
    }

    // --- 有効・無効の切り替え ---

    [Fact]
    public async Task 有効無効を切り替えられる()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request(isEnabled: true));

        var disabled = await sut.SetEnabledAsync(created.Id, false);
        Assert.False(disabled.IsEnabled);

        var enabled = await sut.SetEnabledAsync(created.Id, true);
        Assert.True(enabled.IsEnabled);
    }

    [Fact]
    public async Task 切り替えを監査に残す()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());
        _audit.Entries.Clear();

        await sut.SetEnabledAsync(created.Id, false);

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("diagnostic_rule.set_enabled", entry.Action);
        Assert.Contains("無効", entry.Details);
    }

    [Fact]
    public async Task 無効にしたルールは判定に使われない()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());
        await sut.SetEnabledAsync(created.Id, false);

        var enabled = await _rules.GetEnabledAsync();

        Assert.Empty(enabled);
    }

    // --- 作ったルールが実際に判定へ効くこと ---

    [Fact]
    public async Task 作ったルールがそのまま判定に使える()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Request(
            ruleType: "Threshold",
            conditionJson: """{"field":"diskUsagePercent","operator":">=","value":80}"""));

        var matches = new RuleEngine().Evaluate(
            await _rules.GetEnabledAsync(),
            new DiagnosticContext { DiskUsagePercent = 85 });

        var match = Assert.Single(matches);
        Assert.Contains("85", match.Rationale);
    }

    [Fact]
    public async Task 優先度の小さいルールが先に評価される()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Request(name: "後", priority: 50));
        await sut.CreateAsync(Request(name: "先", priority: 5));

        var enabled = await _rules.GetEnabledAsync();

        Assert.Equal("先", enabled[0].Name);
        Assert.Equal("後", enabled[1].Name);
    }
}
