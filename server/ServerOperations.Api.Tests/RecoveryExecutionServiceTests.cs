using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class RecoveryExecutionServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeRecoveryActionRepository _actions = new();
    private readonly FakeRecoveryApprovalRepository _approvals = new();
    private readonly FakeMonitoringTargetRepository _targets = new();
    private readonly FakeDockerAdapter _docker = new();
    private readonly FakeHealthCheckService _healthCheck = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    private RecoveryExecutionService CreateSut() => new(
        _actions, _approvals, _targets, new AdapterTemplateCatalog(), new RecoveryActionCatalog(),
        _docker, _healthCheck, _time, NullLogger<RecoveryExecutionService>.Instance);

    private void AddTarget(long id = 1, string templateId = "docker-host")
    {
        _targets.Targets.Add(new MonitoringTarget
        {
            Id = id,
            Name = $"t{id}",
            TemplateId = templateId,
            IsEnabled = true,
            AllowedContainersJson = AllowedContainers.Serialize(["web"]),
            Profile = new TargetProfile
            {
                TargetId = id,
                SettingsJson = """{"endpoint":"http://192.168.1.20:2375"}""",
            },
        });
    }

    private RecoveryAction AddAction(
        string actionId = RecoveryActionCatalog.RestartAllowedContainer,
        RecoveryActionStatus status = RecoveryActionStatus.Queued,
        long? approvalId = null,
        string? resource = "web")
    {
        var action = new RecoveryAction
        {
            Id = _actions.Actions.Count + 1,
            IncidentId = 1,
            TargetId = 1,
            ActionId = actionId,
            TargetResource = resource,
            RiskLevel = actionId == RecoveryActionCatalog.StopAllowedContainer
                ? ActionRiskLevel.Medium : ActionRiskLevel.Low,
            Status = status,
            ApprovalId = approvalId,
            RequestedAt = BaseTime.UtcDateTime,
        };
        _actions.Actions.Add(action);
        return action;
    }

    [Fact]
    public async Task Execute_RestartContainer_CallsDockerAndRunsHealthCheck()
    {
        AddTarget();
        var action = AddAction();

        await CreateSut().ExecuteAsync(action.Id);

        var call = Assert.Single(_docker.ControlCalls);
        Assert.Equal("web", call.Container);
        Assert.Equal(ContainerOperation.Restart, call.Operation);
        Assert.Equal(RecoveryActionStatus.Succeeded, action.Status);
        Assert.NotNull(action.CompletedAt);

        // 復旧後に別途ヘルスチェックが実行される
        Assert.Contains(_healthCheck.Calls, c => c.RecoveryActionId == action.Id);
    }

    [Fact]
    public async Task Execute_DockerFailure_MarksActionFailed()
    {
        AddTarget();
        var action = AddAction();
        _docker.ControlResult = new AdapterConnectionResult(false, "操作に失敗しました。");

        await CreateSut().ExecuteAsync(action.Id);

        Assert.Equal(RecoveryActionStatus.Failed, action.Status);
        // 失敗時もヘルスチェックで現状を記録する
        Assert.Contains(_healthCheck.Calls, c => c.RecoveryActionId == action.Id);
    }

    [Fact]
    public async Task Execute_NonQueuedAction_IsSkipped()
    {
        AddTarget();
        var action = AddAction(status: RecoveryActionStatus.Succeeded);

        await CreateSut().ExecuteAsync(action.Id);

        Assert.Empty(_docker.ControlCalls);
    }

    [Fact]
    public async Task Execute_ActionNotAllowedForTemplate_DoesNotTouchDocker()
    {
        // 実行直前の再検証: web-siteテンプレートは再起動を許可していない
        AddTarget(templateId: "web-site");
        var action = AddAction();

        await CreateSut().ExecuteAsync(action.Id);

        Assert.Empty(_docker.ControlCalls);
        Assert.Equal(RecoveryActionStatus.Failed, action.Status);
        Assert.Contains("許可されていない", action.ResultMessage);
    }

    [Fact]
    public async Task Execute_ContainerRemovedFromAllowList_DoesNotTouchDocker()
    {
        // 受付後に許可リストから外された場合、実行直前の再検証で止まること
        AddTarget();
        var action = AddAction();
        _targets.Targets[0].AllowedContainersJson = "[]";

        await CreateSut().ExecuteAsync(action.Id);

        Assert.Empty(_docker.ControlCalls);
        Assert.Equal(RecoveryActionStatus.Failed, action.Status);
        Assert.Contains("許可リスト", action.ResultMessage);
    }

    [Fact]
    public async Task Execute_MediumWithoutApproval_DoesNotTouchDocker()
    {
        AddTarget();
        var action = AddAction(actionId: RecoveryActionCatalog.StopAllowedContainer);

        await CreateSut().ExecuteAsync(action.Id);

        Assert.Empty(_docker.ControlCalls);
        Assert.Equal(RecoveryActionStatus.Failed, action.Status);
    }

    [Fact]
    public async Task Execute_MediumWithExpiredApproval_DoesNotTouchDocker()
    {
        AddTarget();
        _approvals.Approvals.Add(new RecoveryApproval
        {
            Id = 1,
            IncidentId = 1,
            TargetId = 1,
            ActionId = RecoveryActionCatalog.StopAllowedContainer,
            TargetResource = "web",
            Status = ApprovalStatus.Approved,
            ExpiresAt = BaseTime.UtcDateTime.AddMinutes(-1),
            CreatedAt = BaseTime.UtcDateTime.AddMinutes(-31),
        });
        var action = AddAction(actionId: RecoveryActionCatalog.StopAllowedContainer, approvalId: 1);

        await CreateSut().ExecuteAsync(action.Id);

        Assert.Empty(_docker.ControlCalls);
        Assert.Equal(RecoveryActionStatus.Failed, action.Status);
    }

    [Fact]
    public async Task Execute_MediumWithValidApproval_ConsumesApproval()
    {
        AddTarget();
        var approval = new RecoveryApproval
        {
            Id = 1,
            IncidentId = 1,
            TargetId = 1,
            ActionId = RecoveryActionCatalog.StopAllowedContainer,
            TargetResource = "web",
            Status = ApprovalStatus.Approved,
            ExpiresAt = BaseTime.UtcDateTime.AddMinutes(30),
            CreatedAt = BaseTime.UtcDateTime,
        };
        _approvals.Approvals.Add(approval);
        var action = AddAction(actionId: RecoveryActionCatalog.StopAllowedContainer, approvalId: 1);

        await CreateSut().ExecuteAsync(action.Id);

        Assert.Equal(ContainerOperation.Stop, Assert.Single(_docker.ControlCalls).Operation);
        Assert.Equal(RecoveryActionStatus.Succeeded, action.Status);
        // 承認は1回で消費され、再利用できない
        Assert.True(approval.IsConsumed);
    }

    [Fact]
    public async Task Execute_RecheckHttpHealth_RunsHealthCheckWithoutDocker()
    {
        AddTarget(templateId: "web-site");
        var action = AddAction(actionId: RecoveryActionCatalog.RecheckHttpHealth, resource: null);

        await CreateSut().ExecuteAsync(action.Id);

        Assert.Empty(_docker.ControlCalls);
        Assert.Equal(RecoveryActionStatus.Succeeded, action.Status);
    }

    [Fact]
    public async Task Execute_DisabledTarget_DoesNotTouchDocker()
    {
        AddTarget();
        _targets.Targets[0].IsEnabled = false;
        var action = AddAction();

        await CreateSut().ExecuteAsync(action.Id);

        Assert.Empty(_docker.ControlCalls);
        Assert.Equal(RecoveryActionStatus.Failed, action.Status);
    }
}
