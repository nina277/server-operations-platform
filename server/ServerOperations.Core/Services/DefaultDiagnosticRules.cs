using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services;

/// <summary>
/// 初期投入する基本診断ルール(ContainerStopped / HttpUnavailable / MemoryPressure / DiskPressure)。
/// DBにルールが1件もない場合の初回起動時にシードされる。
/// </summary>
public static class DefaultDiagnosticRules
{
    public static List<DiagnosticRule> Create(DateTime nowUtc) =>
    [
        new DiagnosticRule
        {
            Name = "コンテナ停止",
            Classification = "ContainerStopped",
            RuleType = DiagnosticRuleType.State,
            ConditionJson = """{"field":"containerState","equalsAny":["exited","dead"]}""",
            Severity = IncidentSeverity.High,
            RecommendedActionId = "RESTART_ALLOWED_CONTAINER",
            Priority = 10,
            RationaleTemplate = "コンテナ状態({field})が {value} です(判定条件: {expected})。",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        },
        new DiagnosticRule
        {
            Name = "HTTP応答不可",
            Classification = "HttpUnavailable",
            RuleType = DiagnosticRuleType.Threshold,
            ConditionJson = """{"field":"httpSuccess","operator":"==","value":0}""",
            Severity = IncidentSeverity.High,
            RecommendedActionId = "RECHECK_HTTP_HEALTH",
            Priority = 10,
            RationaleTemplate = "HTTPヘルスチェックが失敗しています({field}={value})。",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        },
        new DiagnosticRule
        {
            Name = "メモリ逼迫(ログ検知)",
            Classification = "MemoryPressure",
            RuleType = DiagnosticRuleType.Regex,
            ConditionJson = """{"field":"logExcerpt","pattern":"(?i)out of memory|oom-?kill|cannot allocate memory"}""",
            Severity = IncidentSeverity.High,
            RecommendedActionId = "RESTART_ALLOWED_CONTAINER",
            Priority = 5,
            RationaleTemplate = "ログにメモリ不足の兆候({value})が含まれています(パターン: {expected})。",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        },
        new DiagnosticRule
        {
            Name = "メモリ逼迫(使用率)",
            Classification = "MemoryPressure",
            RuleType = DiagnosticRuleType.Threshold,
            ConditionJson = """{"field":"memoryUsagePercent","operator":">=","value":90}""",
            Severity = IncidentSeverity.Medium,
            RecommendedActionId = null,
            Priority = 20,
            RationaleTemplate = "メモリ使用率が {value}% に達しています(判定条件: {expected})。",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        },
        new DiagnosticRule
        {
            Name = "ディスク逼迫(ログ検知)",
            Classification = "DiskPressure",
            RuleType = DiagnosticRuleType.Regex,
            ConditionJson = """{"field":"logExcerpt","pattern":"(?i)no space left on device|disk full"}""",
            Severity = IncidentSeverity.High,
            RecommendedActionId = null,
            Priority = 5,
            RationaleTemplate = "ログにディスク不足の兆候({value})が含まれています(パターン: {expected})。",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        },
    ];

    // CPU使用率の初期ルールは置かない。
    // ビルドや動画変換のように、100%が正常な使い方であるコンテナは珍しくない。
    // 既定で入れると正常な稼働をインシデントとして流し続けることになるため、
    // 必要な対象にだけ利用者が足す。
    //
    // ディスク使用率の初期ルールも置かない。
    // Docker APIはホストのファイルシステム容量を返さず、収集する手段が無い。
    // ディスク不足は「ディスク逼迫(ログ検知)」で拾う。
}
