using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class AutoRecoveryServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeRecoveryActionRepository _actions = new();
    private readonly FakeAuditLogRepository _auditLogs = new();
    private readonly RecordingExecutionService _execution = new();
    private readonly TestTimeProvider _time = new(BaseTime);
    private readonly RecoveryLimits _limits = new();

    private AutoRecoveryService CreateSut() => new(
        _actions, new RecoveryActionCatalog(),
        new RecoveryRateLimiter(_actions, _limits, _time),
        _execution, _auditLogs, _time, NullLogger<AutoRecoveryService>.Instance);

    private static MonitoringTarget Target(
        bool autoRecoveryEnabled = true, params string[] allowedContainers) => new()
    {
        Id = 1,
        Name = "docker1",
        TemplateId = "docker-host",
        IsEnabled = true,
        AutoRecoveryEnabled = autoRecoveryEnabled,
        AllowedContainersJson = AllowedContainers.Serialize(
            allowedContainers.Length == 0 ? ["web"] : allowedContainers),
    };

    private static Incident Incident(int occurrenceCount = 1) => new()
    {
        Id = 1,
        TargetId = 1,
        SignatureSha256 = "sig",
        Title = "コンテナ web が停止しています",
        Classification = "ContainerStopped",
        Service = "web",
        Severity = IncidentSeverity.High,
        OccurrenceCount = occurrenceCount,
        FirstOccurredAt = BaseTime.UtcDateTime,
        LastOccurredAt = BaseTime.UtcDateTime,
    };

    private static Diagnosis Diagnosis(
        string? actionId = RecoveryActionCatalog.RestartAllowedContainer,
        bool allowed = true) => new()
    {
        Id = 1,
        IncidentId = 1,
        TargetId = 1,
        Source = DiagnosisSource.Rule,
        Classification = "ContainerStopped",
        Severity = IncidentSeverity.High,
        Rationale = "コンテナ状態が exited です。",
        RecommendedActionId = actionId,
        RecommendedActionAllowed = allowed,
        CreatedAt = BaseTime.UtcDateTime,
    };

    [Fact]
    public async Task TryRecover_AllConditionsMet_ExecutesAndAudits()
    {
        var action = await CreateSut().TryRecoverAsync(Target(), Incident(), Diagnosis());

        Assert.NotNull(action);
        Assert.Equal(RecoveryActionCatalog.RestartAllowedContainer, action.ActionId);
        Assert.Equal("web", action.TargetResource);
        Assert.Null(action.RequestedByUserId);
        Assert.Equal(action.Id, Assert.Single(_execution.Executed));
        Assert.Contains(_auditLogs.Logs,
            a => a.Action == "recovery.auto.requested" && a.Result == AuditResult.Success);
    }

    [Fact]
    public async Task TryRecover_AutoRecoveryDisabled_DoesNothing()
    {
        var action = await CreateSut().TryRecoverAsync(
            Target(autoRecoveryEnabled: false), Incident(), Diagnosis());

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
        Assert.Empty(_actions.Actions);
    }

    [Fact]
    public async Task TryRecover_NoRecommendedAction_DoesNothing()
    {
        var action = await CreateSut().TryRecoverAsync(
            Target(), Incident(), Diagnosis(actionId: null));

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
    }

    [Fact]
    public async Task TryRecover_ActionNotAllowedByCapability_DoesNothing()
    {
        var action = await CreateSut().TryRecoverAsync(
            Target(), Incident(), Diagnosis(allowed: false));

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
    }

    [Theory]
    [InlineData(RecoveryActionCatalog.StopAllowedContainer)]
    [InlineData(RecoveryActionCatalog.StartAllowedContainer)]
    public async Task TryRecover_MediumActions_AreNeverAutoExecuted(string actionId)
    {
        var action = await CreateSut().TryRecoverAsync(
            Target(), Incident(), Diagnosis(actionId: actionId));

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
        Assert.Contains(_auditLogs.Logs,
            a => a.Action == "recovery.auto.denied" && a.Result == AuditResult.Denied);
    }

    [Fact]
    public async Task TryRecover_ContainerNotInAllowList_IsDeniedAndAudited()
    {
        var action = await CreateSut().TryRecoverAsync(
            Target(allowedContainers: "api"), Incident(), Diagnosis());

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
        Assert.Contains(_auditLogs.Logs,
            a => a.Action == "recovery.auto.denied" && a.Result == AuditResult.Denied);
    }

    [Fact]
    public async Task TryRecover_WithEmptyAllowList_IsDenied()
    {
        var target = Target();
        target.AllowedContainersJson = "[]";

        var action = await CreateSut().TryRecoverAsync(target, Incident(), Diagnosis());

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
    }

    [Fact]
    public async Task TryRecover_WithinCooldown_IsBlockedWithoutExecuting()
    {
        // 直前に実行済みの履歴を作る
        _actions.Actions.Add(new RecoveryAction
        {
            Id = 1,
            IncidentId = 1,
            TargetId = 1,
            ActionId = RecoveryActionCatalog.RestartAllowedContainer,
            TargetResource = "web",
            RiskLevel = ActionRiskLevel.Low,
            Status = RecoveryActionStatus.Succeeded,
            RequestedAt = BaseTime.UtcDateTime.AddMinutes(-2),
            CompletedAt = BaseTime.UtcDateTime.AddMinutes(-2),
        });

        var action = await CreateSut().TryRecoverAsync(Target(), Incident(occurrenceCount: 2), Diagnosis());

        Assert.NotNull(action);
        Assert.Equal(RecoveryActionStatus.Blocked, action.Status);
        Assert.Equal("cooldown", action.BlockedReason);
        Assert.Empty(_execution.Executed);
        Assert.Contains(_auditLogs.Logs, a => a.Action == "recovery.auto.blocked");
    }

    [Fact]
    public async Task TryRecover_SameOccurrence_DoesNotExecuteTwice()
    {
        var sut = CreateSut();
        var incident = Incident();

        await sut.TryRecoverAsync(Target(), incident, Diagnosis());
        // 同じ発生回数のまま再度呼ばれても、冪等キーが一致するため二重実行しない
        var second = await sut.TryRecoverAsync(Target(), incident, Diagnosis());

        Assert.Null(second);
        Assert.Single(_execution.Executed);
        Assert.Single(_actions.Actions);
    }

    [Fact]
    public async Task TryRecover_RecheckHttpHealth_IsNotAutoExecuted()
    {
        // 副作用のない再チェックは収集で同等の確認が行われるため自動実行しない
        var action = await CreateSut().TryRecoverAsync(
            Target(), Incident(), Diagnosis(actionId: RecoveryActionCatalog.RecheckHttpHealth));

        Assert.Null(action);
        Assert.Empty(_execution.Executed);
    }

    [Fact]
    public async Task TryRecover_AuditRecordsSystemActor()
    {
        await CreateSut().TryRecoverAsync(Target(), Incident(), Diagnosis());

        var entry = _auditLogs.Logs.Single(a => a.Action == "recovery.auto.requested");
        Assert.Null(entry.ActorUserId);
        Assert.Contains("auto-recovery", entry.ActorName);
        Assert.False(string.IsNullOrEmpty(entry.IpAddress));
        Assert.False(string.IsNullOrEmpty(entry.UserAgent));
    }

    private sealed class RecordingExecutionService : IRecoveryExecutionService
    {
        public List<long> Executed { get; } = [];

        public Task ExecuteAsync(long recoveryActionId, CancellationToken ct = default)
        {
            Executed.Add(recoveryActionId);
            return Task.CompletedTask;
        }
    }
}

public class FakeAuditLogRepository : IAuditLogRepository
{
    public List<AuditLog> Logs { get; } = [];

    public Task AddAsync(AuditLog entry, CancellationToken ct = default)
    {
        entry.Id = Logs.Count + 1;
        Logs.Add(entry);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
