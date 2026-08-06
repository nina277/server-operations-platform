using System.Text.Json;
using System.Text.RegularExpressions;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services;

/// <summary>ルール評価への入力。収集データを正規化したもの。未取得の値はnull。</summary>
public record DiagnosticContext
{
    public string? ContainerState { get; init; }

    public string? ContainerName { get; init; }

    public int? RestartCount { get; init; }

    public double? CpuUsagePercent { get; init; }

    public double? MemoryUsagePercent { get; init; }

    public bool? HttpSuccess { get; init; }

    public int? HttpStatus { get; init; }

    public long? HttpLatencyMs { get; init; }

    public string? LogExcerpt { get; init; }

    internal object? GetField(string field) => field switch
    {
        "containerState" => ContainerState,
        "containerName" => ContainerName,
        "restartCount" => RestartCount,
        "cpuUsagePercent" => CpuUsagePercent,
        "memoryUsagePercent" => MemoryUsagePercent,
        "httpSuccess" => HttpSuccess,
        "httpStatus" => HttpStatus,
        "httpLatencyMs" => HttpLatencyMs,
        "logExcerpt" => LogExcerpt,
        _ => null,
    };
}

/// <summary>ルール一致結果。根拠(条件と実値)を含む。</summary>
public record RuleMatch(DiagnosticRule Rule, string Rationale);

public interface IRuleEngine
{
    /// <summary>有効なルールを優先度順に評価し、一致したものをすべて返す。</summary>
    IReadOnlyList<RuleMatch> Evaluate(IEnumerable<DiagnosticRule> rules, DiagnosticContext context);
}

public class RuleEngine : IRuleEngine
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<RuleMatch> Evaluate(IEnumerable<DiagnosticRule> rules, DiagnosticContext context)
    {
        var matches = new List<RuleMatch>();

        foreach (var rule in rules.Where(r => r.IsEnabled).OrderBy(r => r.Priority).ThenBy(r => r.Id))
        {
            var rationale = EvaluateRule(rule, context);
            if (rationale is not null)
            {
                matches.Add(new RuleMatch(rule, rationale));
            }
        }

        return matches;
    }

    /// <summary>一致した場合は根拠文字列、しなかった場合はnullを返す。</summary>
    private static string? EvaluateRule(DiagnosticRule rule, DiagnosticContext context)
    {
        try
        {
            return rule.RuleType switch
            {
                DiagnosticRuleType.State => EvaluateState(rule, context),
                DiagnosticRuleType.Threshold => EvaluateThreshold(rule, context),
                DiagnosticRuleType.Regex => EvaluateRegex(rule, context),
                _ => null,
            };
        }
        catch (Exception ex) when (ex is JsonException or RegexParseException or FormatException)
        {
            // 条件定義が壊れているルールは一致なしとして扱う(評価全体を止めない)
            return null;
        }
    }

    private static string? EvaluateState(DiagnosticRule rule, DiagnosticContext context)
    {
        var condition = JsonSerializer.Deserialize<StateCondition>(rule.ConditionJson, JsonOptions);
        if (condition?.Field is null || condition.EqualsAny is not { Count: > 0 })
        {
            return null;
        }

        var value = context.GetField(condition.Field)?.ToString();
        if (value is null)
        {
            return null;
        }

        if (!condition.EqualsAny.Contains(value, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        return BuildRationale(rule.RationaleTemplate, condition.Field, value,
            expected: string.Join("/", condition.EqualsAny));
    }

    private static string? EvaluateThreshold(DiagnosticRule rule, DiagnosticContext context)
    {
        var condition = JsonSerializer.Deserialize<ThresholdCondition>(rule.ConditionJson, JsonOptions);
        if (condition?.Field is null || condition.Operator is null)
        {
            return null;
        }

        var raw = context.GetField(condition.Field);
        if (raw is null)
        {
            return null;
        }

        double actual = raw switch
        {
            double d => d,
            int i => i,
            long l => l,
            bool b => b ? 1 : 0,
            _ => double.TryParse(raw.ToString(), out var parsed) ? parsed : double.NaN,
        };
        if (double.IsNaN(actual))
        {
            return null;
        }

        var matched = condition.Operator switch
        {
            ">=" => actual >= condition.Value,
            ">" => actual > condition.Value,
            "<=" => actual <= condition.Value,
            "<" => actual < condition.Value,
            "==" => Math.Abs(actual - condition.Value) < 0.0001,
            "!=" => Math.Abs(actual - condition.Value) >= 0.0001,
            _ => false,
        };
        if (!matched)
        {
            return null;
        }

        return BuildRationale(rule.RationaleTemplate, condition.Field, actual.ToString("0.##"),
            expected: $"{condition.Operator} {condition.Value}");
    }

    private static string? EvaluateRegex(DiagnosticRule rule, DiagnosticContext context)
    {
        var condition = JsonSerializer.Deserialize<RegexCondition>(rule.ConditionJson, JsonOptions);
        if (condition?.Field is null || string.IsNullOrEmpty(condition.Pattern))
        {
            return null;
        }

        var value = context.GetField(condition.Field)?.ToString();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var match = Regex.Match(value, condition.Pattern,
            RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));
        if (!match.Success)
        {
            return null;
        }

        return BuildRationale(rule.RationaleTemplate, condition.Field, match.Value,
            expected: condition.Pattern);
    }

    private static string BuildRationale(string template, string field, string value, string expected) =>
        template
            .Replace("{field}", field)
            .Replace("{value}", value)
            .Replace("{expected}", expected);

    private sealed record StateCondition(string? Field, List<string>? EqualsAny);

    private sealed record ThresholdCondition(string? Field, string? Operator, double Value);

    private sealed record RegexCondition(string? Field, string? Pattern);
}
