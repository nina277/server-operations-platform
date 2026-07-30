using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

/// <summary>
/// lab-aioops の障害シナリオ SC-01〜SC-05 を、収集値の入力から
/// 「どう診断され、自動復旧が実行されるか」まで通して確かめる。
///
/// 実環境でのシナリオ実行手順は docs/verification.md と scripts/lab-scenarios.sh にある。
/// ここでは収集結果を模した入力を与え、判断の筋道が仕様どおりであることを確認する。
/// </summary>
public class LabScenarioTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly RuleEngine _ruleEngine = new();
    private readonly FakeRecoveryActionRepository _actions = new();
    private readonly FakeAuditLogRepository _auditLogs = new();
    private readonly RecordingExecutionService _execution = new();
    private readonly FakeNotificationService _notifications = new();
    private readonly TestTimeProvider _time = new(BaseTime);
    private readonly RecoveryLimits _limits = new();

    private static List<DiagnosticRule> Rules() => DefaultDiagnosticRules.Create(BaseTime.UtcDateTime);

    private AutoRecoveryService CreateAutoRecovery()
    {
        _execution.Actions = _actions;
        return new AutoRecoveryService(
            _actions,
            new RecoveryActionCatalog(),
            new RecoveryRateLimiter(_actions, _limits, _time),
            _execution,
            _auditLogs,
            _notifications,
            _time,
            NullLogger<AutoRecoveryService>.Instance);
    }

    private static MonitoringTarget Target(
        bool autoRecoveryEnabled = true,
        params string[] allowedContainers) => new()
    {
        Id = 1,
        Name = "lab-docker",
        TemplateId = "docker-host",
        IsEnabled = true,
        AutoRecoveryEnabled = autoRecoveryEnabled,
        AllowedContainersJson = AllowedContainers.Serialize(
            allowedContainers.Length == 0 ? ["lab-web"] : allowedContainers),
    };

    private static Incident Incident(string classification, IncidentSeverity severity) => new()
    {
        Id = 1,
        TargetId = 1,
        SignatureSha256 = "sig",
        Title = classification,
        Classification = classification,
        Service = "lab-web",
        Severity = severity,
        OccurrenceCount = 1,
        FirstOccurredAt = BaseTime.UtcDateTime,
        LastOccurredAt = BaseTime.UtcDateTime,
    };

    /// <summary>ルール判定の結果から診断を組み立てる(DiagnosisServiceと同じ考え方)。</summary>
    private static Diagnosis DiagnosisFrom(RuleMatch match, bool actionAllowed) => new()
    {
        Id = 1,
        IncidentId = 1,
        TargetId = 1,
        Source = DiagnosisSource.Rule,
        RuleId = match.Rule.Id,
        Classification = match.Rule.Classification,
        Severity = match.Rule.Severity,
        Rationale = match.Rationale,
        RecommendedActionId = match.Rule.RecommendedActionId,
        RecommendedActionAllowed = actionAllowed,
        CreatedAt = BaseTime.UtcDateTime,
    };

    // --- SC-01: コンテナ停止 ---

    [Fact]
    public void SC01_停止したコンテナはContainerStoppedとして再起動を推奨する()
    {
        var matches = _ruleEngine.Evaluate(Rules(), new DiagnosticContext
        {
            ContainerState = "exited",
            ContainerName = "lab-web",
        });

        var match = Assert.Single(matches, m => m.Rule.Classification == "ContainerStopped");
        Assert.Equal(IncidentSeverity.High, match.Rule.Severity);
        Assert.Equal(RecoveryActionCatalog.RestartAllowedContainer, match.Rule.RecommendedActionId);
    }

    [Fact]
    public async Task SC01_許可コンテナかつ自動復旧が有効なら再起動を実行する()
    {
        var match = Assert.Single(
            _ruleEngine.Evaluate(Rules(), new DiagnosticContext
            {
                ContainerState = "exited",
                ContainerName = "lab-web",
            }),
            m => m.Rule.Classification == "ContainerStopped");

        var action = await CreateAutoRecovery().TryRecoverAsync(
            Target(allowedContainers: "lab-web"),
            Incident("ContainerStopped", IncidentSeverity.High),
            DiagnosisFrom(match, actionAllowed: true));

        Assert.NotNull(action);
        Assert.Equal(RecoveryActionCatalog.RestartAllowedContainer, action.ActionId);
        Assert.Equal("lab-web", action.TargetResource);
        // 自動実行も監査に残す
        Assert.Contains(_auditLogs.Logs, a => a.Action == "recovery.auto.requested");
    }

    [Fact]
    public async Task SC01_許可リストに無いコンテナは自動復旧しない()
    {
        var match = Assert.Single(
            _ruleEngine.Evaluate(Rules(), new DiagnosticContext
            {
                ContainerState = "exited",
                ContainerName = "lab-web",
            }),
            m => m.Rule.Classification == "ContainerStopped");

        // 許可リストには別のコンテナだけを入れる
        var action = await CreateAutoRecovery().TryRecoverAsync(
            Target(allowedContainers: "lab-api"),
            Incident("ContainerStopped", IncidentSeverity.High),
            DiagnosisFrom(match, actionAllowed: true));

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
    }

    [Fact]
    public async Task SC01_自動復旧が無効なら実行しない()
    {
        var match = Assert.Single(
            _ruleEngine.Evaluate(Rules(), new DiagnosticContext { ContainerState = "exited" }),
            m => m.Rule.Classification == "ContainerStopped");

        var action = await CreateAutoRecovery().TryRecoverAsync(
            Target(autoRecoveryEnabled: false, allowedContainers: "lab-web"),
            Incident("ContainerStopped", IncidentSeverity.High),
            DiagnosisFrom(match, actionAllowed: true));

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
    }

    // --- SC-02: HTTP 503 ---

    [Fact]
    public void SC02_HTTP失敗はHttpUnavailableとして再確認を推奨する()
    {
        var matches = _ruleEngine.Evaluate(Rules(), new DiagnosticContext
        {
            HttpSuccess = false,
            HttpStatus = 503,
            HttpLatencyMs = 12,
        });

        var match = Assert.Single(matches, m => m.Rule.Classification == "HttpUnavailable");
        // 副作用のない再確認を推奨する(いきなりコンテナを止めない)
        Assert.Equal(RecoveryActionCatalog.RecheckHttpHealth, match.Rule.RecommendedActionId);
    }

    [Fact]
    public void SC02_正常応答に戻ればHttpUnavailableは当たらない()
    {
        var matches = _ruleEngine.Evaluate(Rules(), new DiagnosticContext
        {
            HttpSuccess = true,
            HttpStatus = 200,
        });

        Assert.DoesNotContain(matches, m => m.Rule.Classification == "HttpUnavailable");
    }

    // --- SC-03: メモリ不足 ---

    [Fact]
    public void SC03_OOMのログはMemoryPressureとして検知する()
    {
        var matches = _ruleEngine.Evaluate(Rules(), new DiagnosticContext
        {
            ContainerName = "lab-memory",
            LogExcerpt = "Container lab-memory was killed: Out of memory",
        });

        var match = Assert.Single(
            matches,
            m => m.Rule.Classification == "MemoryPressure"
                && m.Rule.RuleType == DiagnosticRuleType.Regex);
        Assert.Equal(IncidentSeverity.High, match.Rule.Severity);
    }

    [Fact]
    public void SC03_使用率だけが高い場合は深刻度を下げ操作を推奨しない()
    {
        var matches = _ruleEngine.Evaluate(Rules(), new DiagnosticContext
        {
            MemoryUsagePercent = 95,
        });

        var match = Assert.Single(matches, m => m.Rule.Classification == "MemoryPressure");
        Assert.Equal(IncidentSeverity.Medium, match.Rule.Severity);
        // 使用率が高いだけでは何も操作しない
        Assert.Null(match.Rule.RecommendedActionId);
    }

    // --- SC-04: ディスク逼迫 ---

    [Fact]
    public void SC04_ディスク不足のログはDiskPressureとして検知し操作を推奨しない()
    {
        var matches = _ruleEngine.Evaluate(Rules(), new DiagnosticContext
        {
            ContainerName = "lab-disk",
            LogExcerpt = "write failed: No space left on device",
        });

        var match = Assert.Single(
            matches,
            m => m.Rule.Classification == "DiskPressure"
                && m.Rule.RuleType == DiagnosticRuleType.Regex);
        // ディスク逼迫は再起動で直らないため、自動操作の対象にしない
        Assert.Null(match.Rule.RecommendedActionId);
    }

    [Fact]
    public async Task SC04_推奨操作が無い診断では自動復旧しない()
    {
        var match = Assert.Single(
            _ruleEngine.Evaluate(Rules(), new DiagnosticContext
            {
                LogExcerpt = "write failed: No space left on device",
            }),
            m => m.Rule.Classification == "DiskPressure");

        var action = await CreateAutoRecovery().TryRecoverAsync(
            Target(allowedContainers: "lab-disk"),
            Incident("DiskPressure", IncidentSeverity.High),
            DiagnosisFrom(match, actionAllowed: true));

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
    }

    // --- SC-05: 未知のログ ---

    [Fact]
    public void SC05_どのルールにも当たらないログは判定結果を返さない()
    {
        var matches = _ruleEngine.Evaluate(Rules(), new DiagnosticContext
        {
            ContainerName = "lab-unknown-log",
            ContainerState = "running",
            HttpSuccess = true,
            LogExcerpt = "ERR_UNKNOWN_SUBSYSTEM: unexpected state transition 0x5f",
        });

        Assert.Empty(matches);
    }

    [Fact]
    public async Task SC05_推奨操作が対象で許可されていなければ自動復旧しない()
    {
        // AIが再起動を推奨しても、対象の能力で許可されていなければ実行しない
        var diagnosis = new Diagnosis
        {
            Id = 1,
            IncidentId = 1,
            TargetId = 1,
            Source = DiagnosisSource.Ai,
            Classification = "UnknownLog",
            Severity = IncidentSeverity.Medium,
            Rationale = "未知のログです。",
            RecommendedActionId = RecoveryActionCatalog.RestartAllowedContainer,
            RecommendedActionAllowed = false,
            CreatedAt = BaseTime.UtcDateTime,
        };

        var action = await CreateAutoRecovery().TryRecoverAsync(
            Target(allowedContainers: "lab-unknown-log"),
            Incident("UnknownLog", IncidentSeverity.Medium),
            diagnosis);

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
    }

    // --- シナリオ共通の前提 ---

    [Fact]
    public void 危険度の高い操作は許可リストに存在しない()
    {
        var catalog = new RecoveryActionCatalog();

        Assert.DoesNotContain(catalog.GetAll(), d => d.RiskLevel == ActionRiskLevel.High);
    }

    [Fact]
    public async Task 中危険度の操作は自動実行の対象にならない()
    {
        var diagnosis = new Diagnosis
        {
            Id = 1,
            IncidentId = 1,
            TargetId = 1,
            Source = DiagnosisSource.Rule,
            Classification = "ContainerStopped",
            Severity = IncidentSeverity.High,
            Rationale = "停止しています。",
            // 停止操作はMedium(承認が必要)
            RecommendedActionId = RecoveryActionCatalog.StopAllowedContainer,
            RecommendedActionAllowed = true,
            CreatedAt = BaseTime.UtcDateTime,
        };

        var action = await CreateAutoRecovery().TryRecoverAsync(
            Target(allowedContainers: "lab-web"),
            Incident("ContainerStopped", IncidentSeverity.High),
            diagnosis);

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
    }
}
