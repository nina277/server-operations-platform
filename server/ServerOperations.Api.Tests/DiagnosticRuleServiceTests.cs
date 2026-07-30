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
        new(_rules, new RecoveryActionCatalog(), new RuleEngine(), _audit, _currentUser, _time);

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

    [Fact]
    public async Task 根拠の文言を保存して返す()
    {
        // 返さないと編集画面が元の文言を復元できず、保存時に失われる
        var rule = await CreateSut().CreateAsync(Request());

        Assert.Equal("コンテナ状態({field})が {value} です。", rule.RationaleTemplate);
    }

    [Fact]
    public async Task 一覧でも根拠の文言を返す()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Request());

        var all = await sut.GetAllAsync();

        Assert.Equal("コンテナ状態({field})が {value} です。", Assert.Single(all).RationaleTemplate);
    }

    [Fact]
    public async Task 更新で渡した根拠の文言をそのまま保持する()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(Request());

        var updated = await sut.UpdateAsync(created.Id, new SaveDiagnosticRuleRequest
        {
            Name = created.Name,
            Classification = created.Classification,
            RuleType = created.RuleType,
            ConditionJson = created.ConditionJson,
            Severity = created.Severity,
            RecommendedActionId = created.RecommendedActionId,
            Priority = created.Priority,
            RationaleTemplate = "独自の文言: {field} = {value}",
            IsEnabled = created.IsEnabled,
        });

        Assert.Equal("独自の文言: {field} = {value}", updated.RationaleTemplate);
    }

    [Fact]
    public void 既定のルールはそれぞれ固有の文言を持つ()
    {
        // 汎用の文言で上書きすると、この違いが失われる
        var templates = DefaultDiagnosticRules.Create(DateTime.UtcNow)
            .Select(r => r.RationaleTemplate)
            .ToList();

        Assert.Equal(templates.Count, templates.Distinct().Count());
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

    // --- 編集中のルールの試験 ---

    private static CandidateRuleDto Candidate(
        long id = 0,
        string ruleType = "Threshold",
        string conditionJson = """{"field":"diskUsagePercent","operator":">=","value":80}""",
        string severity = "Medium",
        string? recommendedActionId = null,
        int priority = 100) => new()
    {
        Id = id,
        Name = "編集中のルール",
        Classification = "DiskPressure",
        RuleType = ruleType,
        ConditionJson = conditionJson,
        Severity = severity,
        RecommendedActionId = recommendedActionId,
        Priority = priority,
        RationaleTemplate = "{field} が {value} です(判定条件: {expected})。",
    };

    [Fact]
    public async Task 保存していないルールでも判定を試せる()
    {
        var response = await CreateSut().TestAsync(new RuleTestRequest
        {
            DiskUsagePercent = 85,
            CandidateRule = Candidate(),
        });

        var match = Assert.Single(response.Matches);
        Assert.Equal("編集中のルール", match.RuleName);
        Assert.True(match.IsCandidate);
        Assert.Contains("85", match.Rationale);
        // 試験では保存しない
        Assert.Empty(_rules.Rules);
    }

    [Fact]
    public async Task 当たらない入力なら仮ルールも結果に出ない()
    {
        var response = await CreateSut().TestAsync(new RuleTestRequest
        {
            DiskUsagePercent = 10,
            CandidateRule = Candidate(),
        });

        Assert.Empty(response.Matches);
    }

    [Fact]
    public async Task 仮ルールと保存済みルールを見分けられる()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Request(
            name: "保存済み",
            ruleType: "Threshold",
            conditionJson: """{"field":"diskUsagePercent","operator":">=","value":50}""",
            recommendedActionId: null));

        var response = await sut.TestAsync(new RuleTestRequest
        {
            DiskUsagePercent = 85,
            CandidateRule = Candidate(),
        });

        Assert.Equal(2, response.Matches.Count);
        Assert.Single(response.Matches, m => m.RuleName == "保存済み" && !m.IsCandidate);
        Assert.Single(response.Matches, m => m.RuleName == "編集中のルール" && m.IsCandidate);
    }

    [Fact]
    public async Task 編集中の内容が保存済みの同じルールを置き換える()
    {
        var sut = CreateSut();
        // 保存済みは90%以上で当たる
        var saved = await sut.CreateAsync(Request(
            name: "ディスク",
            ruleType: "Threshold",
            conditionJson: """{"field":"diskUsagePercent","operator":">=","value":90}""",
            recommendedActionId: null));

        // 編集中は80%以上へ緩めた状態
        var response = await sut.TestAsync(new RuleTestRequest
        {
            DiskUsagePercent = 85,
            CandidateRule = Candidate(id: saved.Id),
        });

        // 置き換えて評価するため、当たるのは編集中の1件だけ
        var match = Assert.Single(response.Matches);
        Assert.True(match.IsCandidate);
        Assert.Equal(saved.Id, match.RuleId);
    }

    [Fact]
    public async Task 無効にしたルールでも編集中の内容は試せる()
    {
        var sut = CreateSut();
        var saved = await sut.CreateAsync(Request(
            name: "止めてある",
            ruleType: "Threshold",
            conditionJson: """{"field":"diskUsagePercent","operator":">=","value":80}""",
            recommendedActionId: null,
            isEnabled: false));

        var response = await sut.TestAsync(new RuleTestRequest
        {
            DiskUsagePercent = 85,
            CandidateRule = Candidate(id: saved.Id),
        });

        Assert.Single(response.Matches);
    }

    [Fact]
    public async Task 仮ルールを渡さなければ保存済みだけを評価する()
    {
        var sut = CreateSut();
        await sut.CreateAsync(Request(
            name: "保存済み",
            ruleType: "Threshold",
            conditionJson: """{"field":"diskUsagePercent","operator":">=","value":80}""",
            recommendedActionId: null));

        var response = await sut.TestAsync(new RuleTestRequest { DiskUsagePercent = 85 });

        var match = Assert.Single(response.Matches);
        Assert.False(match.IsCandidate);
    }

    [Theory]
    // 保存で拒否されるものは、試験でも拒否されなければならない。
    // 試験だけ緩いと「試験は通るが保存できない」条件が生まれ、確認の意味がなくなる。
    [InlineData("""{"field":"apiToken","operator":">=","value":1}""", "invalid_condition")]
    [InlineData("""{"field":"diskUsagePercent","operator":"LIKE","value":1}""", "invalid_condition")]
    public async Task 保存できない条件は試験でも拒否する(string conditionJson, string expectedCode)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().TestAsync(
            new RuleTestRequest { DiskUsagePercent = 85, CandidateRule = Candidate(conditionJson: conditionJson) }));

        Assert.Equal(expectedCode, ex.Code);
    }

    [Fact]
    public async Task 試験でも重い正規表現は拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().TestAsync(
            new RuleTestRequest
            {
                LogExcerpt = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!",
                CandidateRule = Candidate(
                    ruleType: "Regex",
                    conditionJson: """{"field":"logExcerpt","pattern":"(a+)+$"}"""),
            }));

        Assert.Equal("invalid_condition", ex.Code);
    }

    [Fact]
    public async Task 試験でも許可リスト外の推奨アクションは拒否する()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().TestAsync(
            new RuleTestRequest
            {
                DiskUsagePercent = 85,
                CandidateRule = Candidate(recommendedActionId: "EXEC_ARBITRARY_COMMAND"),
            }));

        Assert.Equal("invalid_recommended_action", ex.Code);
    }

    [Fact]
    public async Task 試験は監査に残さない()
    {
        await CreateSut().TestAsync(new RuleTestRequest
        {
            DiskUsagePercent = 85,
            CandidateRule = Candidate(),
        });

        // 保存も実行もしないため、監査の対象にしない
        Assert.Empty(_audit.Entries);
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
