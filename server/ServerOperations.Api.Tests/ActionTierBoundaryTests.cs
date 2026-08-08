using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

/// <summary>
/// 二層の境界。
///
/// 卒業制作(v1.0)の安全性は「実行できる操作を4つに固定した」ことで説明していた。
/// 運用操作を足すにあたり、その主張を
/// **「無人で動く操作は4つに固定」**へ言い換えたうえで保証する。
///
/// ここで固定するのは1つだけ:
///   **第1層(無人)から第2層(人が起動する運用操作)へ到達できない。**
///
/// この試験が通っている限り、第2層に何を足しても
/// 「ログに文字列を書き込める者が到達できる操作」は増えない。
///
/// 設計は docs/extension-design.md にある。
/// </summary>
public class ActionTierBoundaryTests
{
    private readonly RecoveryActionCatalog _catalog = new();

    [Fact]
    public void 無人で実行しうるアクションは4つだけ()
    {
        // 増やすときは docs/extension-design.md の判断を見直すこと。
        // **ここを黙って増やすと、AIとログから到達できる操作が増える。**
        var automatic = _catalog.GetAutomatic();

        Assert.Equal(4, automatic.Count);
        Assert.Equal(
            [
                RecoveryActionCatalog.RecheckHttpHealth,
                RecoveryActionCatalog.RestartAllowedContainer,
                RecoveryActionCatalog.StartAllowedContainer,
                RecoveryActionCatalog.StopAllowedContainer,
            ],
            automatic.Select(a => a.ActionId).OrderBy(id => id, StringComparer.Ordinal).Order().ToArray());
    }

    [Fact]
    public void 第1層に高危険度のアクションは存在しない()
    {
        Assert.DoesNotContain(
            _catalog.GetAutomatic(), a => a.RiskLevel == ActionRiskLevel.High);
    }

    [Fact]
    public void 第1層で承認不要なのはLow危険度だけ()
    {
        // 承認不要 = 人を介さず動きうる。ここにMedium以上が混ざると
        // 無人で中危険度の操作が走る
        foreach (var action in _catalog.GetAutomatic().Where(a => !a.RequiresApproval))
        {
            Assert.Equal(ActionRiskLevel.Low, action.RiskLevel);
        }
    }

    [Fact]
    public void 第2層のアクションは無人実行の判定を通らない()
    {
        foreach (var action in _catalog.GetAll().Where(a => a.Tier == ActionTier.Operator))
        {
            Assert.False(
                _catalog.IsAutomatic(action.ActionId),
                $"{action.ActionId} が無人実行の対象になっています。");
        }
    }

    [Fact]
    public void 存在しないアクションは無人実行の対象にならない()
    {
        Assert.False(_catalog.IsAutomatic("RUN_SHELL"));
        Assert.False(_catalog.IsAutomatic("EXEC_ARBITRARY_COMMAND"));
        Assert.False(_catalog.IsAutomatic(string.Empty));
    }

    [Fact]
    public void 第1層と第2層の合計が全体と一致する()
    {
        // 層を持たないアクションが紛れ込むと、どちらの規則も適用されない
        var all = _catalog.GetAll();
        var automatic = all.Count(a => a.Tier == ActionTier.Automatic);
        var operatorTier = all.Count(a => a.Tier == ActionTier.Operator);

        Assert.Equal(all.Count, automatic + operatorTier);
    }
}
