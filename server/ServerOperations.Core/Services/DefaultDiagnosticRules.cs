using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services;

/// <summary>
/// 初期投入する基本診断ルール(ContainerStopped / HttpUnavailable / MemoryPressure / DiskPressure)。
/// DBにルールが1件もない場合の初回起動時にシードされる。
/// </summary>
public static class DefaultDiagnosticRules
{
    /// <summary>
    /// まだ登録されていない既定ルールだけを返す。
    ///
    /// 「テーブルが空のときだけ入れる」にすると、版を上げて既定ルールが増えても
    /// **既に動いている環境には永久に届かない。**
    /// 新しい収集を足しても、それを見る検知が入らないままになる。
    ///
    /// 名前で照合する。利用者が無効にしたルールは行として残るため、
    /// 無効にしたものが復活することはない
    /// (ルールを削除する口は用意しておらず、無効化までにとどめてある)。
    /// </summary>
    public static List<DiagnosticRule> Missing(IEnumerable<string> existingNames, DateTime nowUtc)
    {
        var existing = new HashSet<string>(existingNames, StringComparer.Ordinal);
        return Create(nowUtc).Where(rule => !existing.Contains(rule.Name)).ToList();
    }

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
        new DiagnosticRule
        {
            Name = "ディスク逼迫(使用率)",
            Classification = "DiskPressure",
            RuleType = DiagnosticRuleType.Threshold,
            ConditionJson = """{"field":"diskUsagePercent","operator":">=","value":85}""",
            Severity = IncidentSeverity.Medium,
            RecommendedActionId = null,
            Priority = 20,
            RationaleTemplate = "ディスク使用率が {value}% に達しています(判定条件: {expected})。",
            CreatedAt = nowUtc,
            UpdatedAt = nowUtc,
        },
    ];

    // CPU使用率の初期ルールは置かない。
    // ビルドや動画変換のように、100%が正常な使い方であるコンテナは珍しくない。
    // 既定で入れると正常な稼働をインシデントとして流し続けることになるため、
    // 必要な対象にだけ利用者が足す。
    //
    // ディスクだけメモリより低い85%にしてある。
    // メモリは逼迫しても解放されれば戻るが、ディスクは自然には減らない。
    // 満杯になってから気づいても、消すものを選ぶ余裕が無い。
}
