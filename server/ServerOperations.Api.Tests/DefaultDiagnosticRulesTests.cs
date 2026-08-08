using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

/// <summary>
/// 既定の診断ルールの投入。
///
/// 「テーブルが空のときだけ入れる」にすると、版を上げて既定ルールが増えても
/// 既に動いている環境には永久に届かない。
/// 収集を足しても、それを見る検知が入らないままになる。
/// </summary>
public class DefaultDiagnosticRulesTests
{
    private static readonly DateTime Now = new(2026, 8, 6, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void 何も入っていなければすべて投入する()
    {
        var missing = DefaultDiagnosticRules.Missing([], Now);

        Assert.Equal(DefaultDiagnosticRules.Create(Now).Count, missing.Count);
    }

    [Fact]
    public void すべて揃っていれば何も投入しない()
    {
        var names = DefaultDiagnosticRules.Create(Now).Select(rule => rule.Name);

        Assert.Empty(DefaultDiagnosticRules.Missing(names, Now));
    }

    [Fact]
    public void 足りない分だけ投入する()
    {
        // 版を上げて既定ルールが増えた状況
        var all = DefaultDiagnosticRules.Create(Now);
        var names = all.Skip(1).Select(rule => rule.Name);

        var missing = DefaultDiagnosticRules.Missing(names, Now);

        var added = Assert.Single(missing);
        Assert.Equal(all[0].Name, added.Name);
    }

    [Fact]
    public void 名前で照合するので無効にしたルールは復活しない()
    {
        // 呼び出し側は有効・無効に関わらず全件の名前を渡す。
        // 無効にしたルールも行として残るため、名前が一致して投入されない。
        // (ルールを削除する口は用意しておらず、無効化までにとどめてある)
        var names = DefaultDiagnosticRules.Create(Now).Select(rule => rule.Name).ToList();

        Assert.Empty(DefaultDiagnosticRules.Missing(names, Now));
    }

    [Fact]
    public void 既定ルールの名前は重複しない()
    {
        // 名前で照合するため、既定ルール同士で重複すると投入が安定しない
        var names = DefaultDiagnosticRules.Create(Now).Select(rule => rule.Name).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void 既定ルールの条件はすべて保存時の検証を通る()
    {
        // 検証を通らない条件を既定に置くと、画面から開いた瞬間に保存できなくなる
        foreach (var rule in DefaultDiagnosticRules.Create(Now))
        {
            var result = RuleConditionValidator.Validate(rule.RuleType, rule.ConditionJson);

            Assert.True(result.IsValid, $"{rule.Name}: {result.Error}");
        }
    }

    [Fact]
    public void 既定ルールが推奨する操作は許可リストにあるものだけ()
    {
        // 存在しない操作を推奨すると、診断に「実行できない推奨」が並ぶ
        var allowed = new RecoveryActionCatalog().GetAll()
            .Select(action => action.ActionId)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var rule in DefaultDiagnosticRules.Create(Now).Where(r => r.RecommendedActionId is not null))
        {
            Assert.Contains(rule.RecommendedActionId!, allowed);
        }
    }

    [Fact]
    public void 使用率のルールは操作を推奨しない()
    {
        // 使用率が高いだけでは何を操作すれば直るか定まらない。
        // 再起動してもディスクの容量は戻らない。
        var usageRules = DefaultDiagnosticRules.Create(Now)
            .Where(rule => rule.RuleType == DiagnosticRuleType.Threshold
                && rule.ConditionJson.Contains("UsagePercent", StringComparison.Ordinal));

        Assert.All(usageRules, rule => Assert.Null(rule.RecommendedActionId));
    }
}
