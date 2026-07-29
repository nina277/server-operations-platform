using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class RuleEngineTests
{
    private static readonly DateTime Now = new(2026, 7, 10, 12, 0, 0, DateTimeKind.Utc);

    private readonly RuleEngine _sut = new();

    private static List<DiagnosticRule> DefaultRules() => DefaultDiagnosticRules.Create(Now);

    [Fact]
    public void ContainerStopped_StateRule_Matches()
    {
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext
        {
            ContainerState = "exited",
            ContainerName = "web",
        });

        var match = Assert.Single(matches, m => m.Rule.Classification == "ContainerStopped");
        Assert.Contains("exited", match.Rationale);
        Assert.Equal("RESTART_ALLOWED_CONTAINER", match.Rule.RecommendedActionId);
    }

    [Fact]
    public void RunningContainer_DoesNotMatchContainerStopped()
    {
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext { ContainerState = "running" });

        Assert.DoesNotContain(matches, m => m.Rule.Classification == "ContainerStopped");
    }

    [Fact]
    public void HttpUnavailable_ThresholdRule_MatchesOnFailure()
    {
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext { HttpSuccess = false });

        var match = Assert.Single(matches, m => m.Rule.Classification == "HttpUnavailable");
        Assert.Equal("RECHECK_HTTP_HEALTH", match.Rule.RecommendedActionId);
    }

    [Fact]
    public void HttpUnavailable_DoesNotMatchOnSuccess()
    {
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext { HttpSuccess = true });

        Assert.DoesNotContain(matches, m => m.Rule.Classification == "HttpUnavailable");
    }

    [Theory]
    [InlineData("container killed due to Out of memory")]
    [InlineData("oom-kill triggered by cgroup")]
    [InlineData("fatal: cannot allocate memory")]
    public void MemoryPressure_RegexRule_MatchesOomLogs(string log)
    {
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext { LogExcerpt = log });

        Assert.Contains(matches, m => m.Rule.Classification == "MemoryPressure");
    }

    [Fact]
    public void DiskPressure_RegexRule_MatchesNoSpaceLog()
    {
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext
        {
            LogExcerpt = "write failed: No space left on device",
        });

        Assert.Contains(matches, m => m.Rule.Classification == "DiskPressure");
    }

    [Theory]
    [InlineData(95.0, true)]
    [InlineData(90.0, true)]
    [InlineData(80.0, false)]
    public void MemoryPressure_ThresholdRule_RespectsBoundary(double usage, bool shouldMatch)
    {
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext { MemoryUsagePercent = usage });

        Assert.Equal(shouldMatch, matches.Any(m => m.Rule.Classification == "MemoryPressure"));
    }

    [Fact]
    public void Matches_AreOrderedByPriority()
    {
        // メモリ逼迫(ログ検知, Priority=5)がコンテナ停止(Priority=10)より先に来る
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext
        {
            ContainerState = "exited",
            LogExcerpt = "container killed due to Out of memory",
        });

        Assert.Equal("MemoryPressure", matches[0].Rule.Classification);
        Assert.Equal("ContainerStopped", matches[1].Rule.Classification);
    }

    [Fact]
    public void MissingField_DoesNotMatch()
    {
        // 収集できなかった項目(null)は一致しない = 未取得を異常と誤判定しない
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext());

        Assert.Empty(matches);
    }

    [Fact]
    public void DisabledRule_IsSkipped()
    {
        var rules = DefaultRules();
        foreach (var rule in rules)
        {
            rule.IsEnabled = false;
        }

        var matches = _sut.Evaluate(rules, new DiagnosticContext { ContainerState = "exited" });

        Assert.Empty(matches);
    }

    [Fact]
    public void MalformedCondition_IsSkipped_WithoutBreakingOtherRules()
    {
        var rules = DefaultRules();
        rules.Insert(0, new DiagnosticRule
        {
            Id = 999,
            Name = "壊れたルール",
            Classification = "Broken",
            RuleType = DiagnosticRuleType.Threshold,
            ConditionJson = "{ not valid json",
            Priority = 1,
            RationaleTemplate = "x",
            CreatedAt = Now,
            UpdatedAt = Now,
        });

        var matches = _sut.Evaluate(rules, new DiagnosticContext { ContainerState = "exited" });

        Assert.DoesNotContain(matches, m => m.Rule.Classification == "Broken");
        Assert.Contains(matches, m => m.Rule.Classification == "ContainerStopped");
    }

    [Fact]
    public void Rationale_ContainsConditionAndActualValue()
    {
        var matches = _sut.Evaluate(DefaultRules(), new DiagnosticContext { MemoryUsagePercent = 93.5 });

        var match = Assert.Single(matches, m => m.Rule.RuleType == DiagnosticRuleType.Threshold
            && m.Rule.Classification == "MemoryPressure");
        Assert.Contains("93.5", match.Rationale);
        Assert.Contains(">= 90", match.Rationale);
    }
}
