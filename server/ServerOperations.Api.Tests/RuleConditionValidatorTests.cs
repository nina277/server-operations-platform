using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class RuleConditionValidatorTests
{
    // --- 共通 ---

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void 条件が空なら拒否する(string? json)
    {
        var result = RuleConditionValidator.Validate(DiagnosticRuleType.State, json);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void JSONでない条件は拒否する()
    {
        var result = RuleConditionValidator.Validate(DiagnosticRuleType.State, "これは条件です");

        Assert.False(result.IsValid);
        Assert.Contains("JSON", result.Error);
    }

    [Theory]
    [InlineData(DiagnosticRuleType.State, """{"field":"unknownField","equalsAny":["x"]}""")]
    [InlineData(DiagnosticRuleType.Threshold, """{"field":"password","operator":">=","value":1}""")]
    [InlineData(DiagnosticRuleType.Regex, """{"field":"apiToken","pattern":"x"}""")]
    public void 既知でない項目は拒否する(DiagnosticRuleType ruleType, string json)
    {
        var result = RuleConditionValidator.Validate(ruleType, json);

        Assert.False(result.IsValid);
        Assert.Contains("項目", result.Error);
    }

    [Fact]
    public void 参照できる項目は収集値に限る()
    {
        // 秘密情報や任意のプロパティを条件から読ませない
        Assert.DoesNotContain("password", RuleConditionValidator.AllowedFields);
        Assert.DoesNotContain("apiToken", RuleConditionValidator.AllowedFields);
        Assert.Contains("containerState", RuleConditionValidator.AllowedFields);
        Assert.Contains("logExcerpt", RuleConditionValidator.AllowedFields);
    }

    [Fact]
    public void 収集しない項目は参照できない()
    {
        // ディスク使用率はDocker APIから取れない。
        // 参照できるままにすると、当たらないルールを作れてしまう。
        // 画面にはルールが並ぶため監視できているように見え、実際には何も検知しない。
        Assert.DoesNotContain("diskUsagePercent", RuleConditionValidator.AllowedFields);

        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.Threshold,
            """{"field":"diskUsagePercent","operator":">=","value":90}""");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void 参照できる項目はすべて評価できる()
    {
        // 一覧にあるのに評価側が知らない項目があると、常に一致しないルールになる
        var context = new DiagnosticContext
        {
            ContainerState = "exited",
            ContainerName = "web",
            RestartCount = 1,
            CpuUsagePercent = 1,
            MemoryUsagePercent = 1,
            HttpSuccess = false,
            HttpStatus = 503,
            HttpLatencyMs = 1,
            LogExcerpt = "x",
        };

        Assert.All(
            RuleConditionValidator.AllowedFields,
            field => Assert.NotNull(context.GetField(field)));
    }

    // --- State ---

    [Fact]
    public void 状態条件は正しければ通る()
    {
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.State,
            """{"field":"containerState","equalsAny":["exited","dead"]}""");

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("""{"field":"containerState"}""")]
    [InlineData("""{"field":"containerState","equalsAny":[]}""")]
    public void 状態条件で比較値が無ければ拒否する(string json)
    {
        var result = RuleConditionValidator.Validate(DiagnosticRuleType.State, json);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void 状態条件で空の比較値は拒否する()
    {
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.State,
            """{"field":"containerState","equalsAny":["exited",""]}""");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void 状態条件で比較値が多すぎれば拒否する()
    {
        var values = string.Join(',', Enumerable.Range(0, 25).Select(i => $"\"v{i}\""));
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.State,
            $$"""{"field":"containerState","equalsAny":[{{values}}]}""");

        Assert.False(result.IsValid);
    }

    // --- Threshold ---

    [Theory]
    [InlineData(">=")]
    [InlineData(">")]
    [InlineData("<=")]
    [InlineData("<")]
    [InlineData("==")]
    [InlineData("!=")]
    public void しきい値条件は既知の演算子なら通る(string op)
    {
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.Threshold,
            $$"""{"field":"memoryUsagePercent","operator":"{{op}}","value":90}""");

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("=~")]
    [InlineData("LIKE")]
    [InlineData("; DROP TABLE rules")]
    public void しきい値条件で未知の演算子は拒否する(string op)
    {
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.Threshold,
            $$"""{"field":"memoryUsagePercent","operator":"{{op}}","value":90}""");

        Assert.False(result.IsValid);
        Assert.Contains("演算子", result.Error);
    }

    [Fact]
    public void しきい値条件で演算子が無ければ拒否する()
    {
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.Threshold,
            """{"field":"memoryUsagePercent","value":90}""");

        Assert.False(result.IsValid);
    }

    // --- Regex ---

    [Fact]
    public void 正規表現条件は正しければ通る()
    {
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.Regex,
            """{"field":"logExcerpt","pattern":"(?i)out of memory|oom-?kill"}""");

        Assert.True(result.IsValid);
    }

    [Fact]
    public void 解釈できない正規表現は拒否する()
    {
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.Regex,
            """{"field":"logExcerpt","pattern":"([unclosed"}""");

        Assert.False(result.IsValid);
        Assert.Contains("正規表現", result.Error);
    }

    [Fact]
    public void 空のパターンは拒否する()
    {
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.Regex,
            """{"field":"logExcerpt","pattern":""}""");

        Assert.False(result.IsValid);
    }

    [Fact]
    public void 長すぎるパターンは拒否する()
    {
        var pattern = new string('a', 600);
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.Regex,
            $$"""{"field":"logExcerpt","pattern":"{{pattern}}"}""");

        Assert.False(result.IsValid);
        Assert.Contains("長すぎ", result.Error);
    }

    [Theory]
    // バックトラッキングが爆発する書き方。
    // (a|a)*$ のように、.NETのエンジンが最適化して速く終えるものは対象外。
    // 検証器は「書き方の見た目」ではなく「実際に時間がかかるか」で判断する。
    [InlineData(@"(a+)+$")]
    [InlineData(@"(a*)*$")]
    [InlineData(@"(.*a){20}$")]
    public void 評価に時間がかかりすぎるパターンは拒否する(string pattern)
    {
        var escaped = System.Text.Json.JsonSerializer.Serialize(pattern);
        var result = RuleConditionValidator.Validate(
            DiagnosticRuleType.Regex,
            $$"""{"field":"logExcerpt","pattern":{{escaped}}}""");

        Assert.False(result.IsValid);
        Assert.Contains("時間", result.Error);
    }

    [Fact]
    public void 実用的なパターンは拒否しない()
    {
        // 誤って普通のパターンまで弾いていないことを確かめる。
        // (a|a)*$ は見た目こそ危ういが、.NETでは速く終わるため拒否しない。
        string[] patterns =
        [
            "(?i)no space left on device|disk full",
            @"connection refused",
            @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}",
            @"error\s+code=\w+",
            "(?i)out of memory|oom-?kill|cannot allocate memory",
            "(a|a)*$",
        ];

        foreach (var pattern in patterns)
        {
            var escaped = System.Text.Json.JsonSerializer.Serialize(pattern);
            var result = RuleConditionValidator.Validate(
                DiagnosticRuleType.Regex,
                $$"""{"field":"logExcerpt","pattern":{{escaped}}}""");

            Assert.True(result.IsValid, $"拒否されました: {pattern} / {result.Error}");
        }
    }

    [Fact]
    public void 既定のルールはすべて検証を通る()
    {
        var rules = DefaultDiagnosticRules.Create(DateTime.UtcNow);

        foreach (var rule in rules)
        {
            var result = RuleConditionValidator.Validate(rule.RuleType, rule.ConditionJson);
            Assert.True(result.IsValid, $"{rule.Name}: {result.Error}");
        }
    }
}
