using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services;

public record RuleConditionValidationResult(bool IsValid, string? Error = null)
{
    public static RuleConditionValidationResult Valid() => new(true);

    public static RuleConditionValidationResult Invalid(string error) => new(false, error);
}

/// <summary>
/// 診断ルールの条件を保存前に検証する。
///
/// ルールは自動復旧の入口にあたるため、条件を自由記述のまま受け取らない。
/// 種別ごとに構造を確かめ、評価できない条件や重すぎる正規表現を保存させない。
/// </summary>
public static class RuleConditionValidator
{
    /// <summary>条件で参照できる項目。DiagnosticContextが持つものだけを許す。</summary>
    public static readonly IReadOnlyList<string> AllowedFields =
    [
        "containerState",
        "containerName",
        "restartCount",
        "memoryUsagePercent",
        "diskUsagePercent",
        "httpSuccess",
        "httpStatus",
        "httpLatencyMs",
        "logExcerpt",
    ];

    /// <summary>しきい値条件で使える演算子。</summary>
    public static readonly IReadOnlyList<string> AllowedOperators = [">=", ">", "<=", "<", "==", "!="];

    /// <summary>正規表現の最大長。極端に長いパターンは受け付けない。</summary>
    private const int MaxPatternLength = 500;

    /// <summary>正規表現の評価に許す時間。これを超えるものは保存させない。</summary>
    private static readonly TimeSpan PatternTimeout = TimeSpan.FromMilliseconds(200);

    /// <summary>
    /// 正規表現の重さを測るための入力。
    /// 破滅的バックトラッキングを起こすパターンは、この程度の長さでも時間切れになる。
    /// </summary>
    private const string PatternProbeInput =
        "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa!"
        + " error: connection refused 2026-07-10T12:00:00Z host=example.internal";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static RuleConditionValidationResult Validate(DiagnosticRuleType ruleType, string? conditionJson)
    {
        if (string.IsNullOrWhiteSpace(conditionJson))
        {
            return RuleConditionValidationResult.Invalid("条件が空です。");
        }

        return ruleType switch
        {
            DiagnosticRuleType.State => ValidateState(conditionJson),
            DiagnosticRuleType.Threshold => ValidateThreshold(conditionJson),
            DiagnosticRuleType.Regex => ValidateRegex(conditionJson),
            _ => RuleConditionValidationResult.Invalid("ルール種別が不正です。"),
        };
    }

    private static RuleConditionValidationResult ValidateState(string json)
    {
        if (!TryDeserialize<StateCondition>(json, out var condition, out var error))
        {
            return RuleConditionValidationResult.Invalid(error);
        }

        var fieldError = ValidateField(condition!.Field);
        if (fieldError is not null)
        {
            return RuleConditionValidationResult.Invalid(fieldError);
        }

        if (condition.EqualsAny is not { Count: > 0 })
        {
            return RuleConditionValidationResult.Invalid("比較する値を1つ以上指定してください。");
        }

        if (condition.EqualsAny.Count > 20)
        {
            return RuleConditionValidationResult.Invalid("比較する値が多すぎます(20件まで)。");
        }

        if (condition.EqualsAny.Any(string.IsNullOrWhiteSpace))
        {
            return RuleConditionValidationResult.Invalid("空の比較値は指定できません。");
        }

        if (condition.EqualsAny.Any(v => v.Length > 100))
        {
            return RuleConditionValidationResult.Invalid("比較値が長すぎます(100文字まで)。");
        }

        return RuleConditionValidationResult.Valid();
    }

    private static RuleConditionValidationResult ValidateThreshold(string json)
    {
        if (!TryDeserialize<ThresholdCondition>(json, out var condition, out var error))
        {
            return RuleConditionValidationResult.Invalid(error);
        }

        var fieldError = ValidateField(condition!.Field);
        if (fieldError is not null)
        {
            return RuleConditionValidationResult.Invalid(fieldError);
        }

        if (condition.Operator is null || !AllowedOperators.Contains(condition.Operator))
        {
            return RuleConditionValidationResult.Invalid(
                $"演算子は次のいずれかを指定してください: {string.Join(" ", AllowedOperators)}");
        }

        if (double.IsNaN(condition.Value) || double.IsInfinity(condition.Value))
        {
            return RuleConditionValidationResult.Invalid("しきい値が数値ではありません。");
        }

        return RuleConditionValidationResult.Valid();
    }

    private static RuleConditionValidationResult ValidateRegex(string json)
    {
        if (!TryDeserialize<RegexCondition>(json, out var condition, out var error))
        {
            return RuleConditionValidationResult.Invalid(error);
        }

        var fieldError = ValidateField(condition!.Field);
        if (fieldError is not null)
        {
            return RuleConditionValidationResult.Invalid(fieldError);
        }

        if (string.IsNullOrWhiteSpace(condition.Pattern))
        {
            return RuleConditionValidationResult.Invalid("パターンが空です。");
        }

        if (condition.Pattern.Length > MaxPatternLength)
        {
            return RuleConditionValidationResult.Invalid(
                $"パターンが長すぎます({MaxPatternLength}文字まで)。");
        }

        Regex regex;
        try
        {
            regex = new Regex(condition.Pattern, RegexOptions.CultureInvariant, PatternTimeout);
        }
        catch (ArgumentException)
        {
            return RuleConditionValidationResult.Invalid("パターンが正規表現として解釈できません。");
        }

        // 実際に評価してみて、時間がかかりすぎるパターンを保存させない。
        // 評価時にも同じ制限がかかるが、そこで毎回時間切れになるルールは登録させたくない。
        try
        {
            var stopwatch = Stopwatch.StartNew();
            regex.IsMatch(PatternProbeInput);
            stopwatch.Stop();

            if (stopwatch.Elapsed >= PatternTimeout)
            {
                return RuleConditionValidationResult.Invalid(
                    "パターンの評価に時間がかかりすぎます。より単純な書き方にしてください。");
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return RuleConditionValidationResult.Invalid(
                "パターンの評価に時間がかかりすぎます。より単純な書き方にしてください。");
        }

        return RuleConditionValidationResult.Valid();
    }

    private static string? ValidateField(string? field)
    {
        if (string.IsNullOrWhiteSpace(field))
        {
            return "対象の項目を指定してください。";
        }

        return AllowedFields.Contains(field)
            ? null
            : $"対象の項目が不正です。指定できるのは次のみです: {string.Join(", ", AllowedFields)}";
    }

    private static bool TryDeserialize<T>(string json, out T? value, out string error) where T : class
    {
        try
        {
            value = JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch (JsonException)
        {
            value = null;
            error = "条件がJSON形式ではありません。";
            return false;
        }

        if (value is null)
        {
            error = "条件を解釈できません。";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private sealed record StateCondition(string? Field, List<string>? EqualsAny);

    private sealed record ThresholdCondition(string? Field, string? Operator, double Value);

    private sealed record RegexCondition(string? Field, string? Pattern);
}
