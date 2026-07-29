using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class RecoveryServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeIncidentRepository _incidents = new();
    private readonly FakeMonitoringTargetRepository _targets = new();
    private readonly FakeRecoveryActionRepository _actions = new();
    private readonly FakeRecoveryApprovalRepository _approvals = new();
    private readonly FakeHealthCheckRepository _healthChecks = new();
    private readonly FakeRecoveryJobQueue _queue = new();
    private readonly FakeHealthCheckService _healthCheckService = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(BaseTime);
    private readonly RecoveryLimits _limits = new();

    private RecoveryService CreateSut() => new(
        _incidents, _targets, _actions, _approvals, _healthChecks,
        new AdapterTemplateCatalog(), new RecoveryActionCatalog(),
        new RecoveryRateLimiter(_actions, _limits, _time),
        _queue, _healthCheckService, _audit, _currentUser, _limits, _time);

    private Incident AddIncident(long id = 1, long targetId = 1)
    {
        var incident = new Incident
        {
            Id = id,
            TargetId = targetId,
            SignatureSha256 = "sig",
            Title = "コンテナ停止",
            Classification = "ContainerStopped",
            Service = "web",
            FirstOccurredAt = BaseTime.UtcDateTime,
            LastOccurredAt = BaseTime.UtcDateTime,
        };
        _incidents.Incidents.Add(incident);
        return incident;
    }

    private void AddTarget(long id = 1, string templateId = "docker-host")
    {
        _targets.Targets.Add(new MonitoringTarget
        {
            Id = id,
            Name = $"t{id}",
            TemplateId = templateId,
            IsEnabled = true,
        });
    }

    private RecoveryApproval AddApproval(
        string actionId = RecoveryActionCatalog.StopAllowedContainer,
        string? resource = "web",
        ApprovalStatus status = ApprovalStatus.Approved,
        bool consumed = false,
        int expiryMinutes = 30)
    {
        var approval = new RecoveryApproval
        {
            Id = _approvals.Approvals.Count + 1,
            IncidentId = 1,
            TargetId = 1,
            ActionId = actionId,
            TargetResource = resource,
            Status = status,
            IsConsumed = consumed,
            ExpiresAt = BaseTime.UtcDateTime.AddMinutes(expiryMinutes),
            CreatedAt = BaseTime.UtcDateTime,
        };
        _approvals.Approvals.Add(approval);
        return approval;
    }

    [Fact]
    public async Task Request_LowAction_QueuesAndDispatchesToWorker()
    {
        AddTarget();
        AddIncident();

        var dto = await CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
        {
            ActionId = RecoveryActionCatalog.RestartAllowedContainer,
            TargetResource = "web",
        }, idempotencyKey: "key-1");

        Assert.Equal("Queued", dto.Status);
        Assert.Equal("Low", dto.RiskLevel);
        Assert.Equal(dto.Id, Assert.Single(_queue.Enqueued));
        Assert.Contains(_audit.Entries, e => e.Action == "recovery.action.requested");
    }

    [Fact]
    public async Task Request_UnknownAction_Rejects()
    {
        AddTarget();
        AddIncident();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
            {
                ActionId = "RUN_ARBITRARY_COMMAND",
            }, idempotencyKey: "key-1"));

        Assert.Equal("unknown_action", ex.Code);
        Assert.Empty(_queue.Enqueued);
    }

    [Theory]
    [InlineData("DB_RESTART")]
    [InlineData("VM_DELETE")]
    [InlineData("EXEC")]
    public async Task Request_HighRiskActions_AreNotInCatalog(string actionId)
    {
        AddTarget();
        AddIncident();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest { ActionId = actionId },
                idempotencyKey: "key-1"));

        // High操作はカタログに存在しないため未知の操作として拒否される
        Assert.Equal("unknown_action", ex.Code);
        Assert.Empty(_actions.Actions);
    }

    [Fact]
    public async Task Request_WithoutIdempotencyKey_Rejects()
    {
        AddTarget();
        AddIncident();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
            {
                ActionId = RecoveryActionCatalog.RestartAllowedContainer,
                TargetResource = "web",
            }, idempotencyKey: null));

        Assert.Equal("idempotency_key_required", ex.Code);
    }

    [Fact]
    public async Task Request_SameIdempotencyKey_DoesNotExecuteTwice()
    {
        AddTarget();
        AddIncident();
        var sut = CreateSut();

        var first = await sut.RequestActionAsync(1, new CreateRecoveryActionRequest
        {
            ActionId = RecoveryActionCatalog.RestartAllowedContainer,
            TargetResource = "web",
        }, idempotencyKey: "same-key");

        var second = await sut.RequestActionAsync(1, new CreateRecoveryActionRequest
        {
            ActionId = RecoveryActionCatalog.RestartAllowedContainer,
            TargetResource = "web",
        }, idempotencyKey: "same-key");

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_actions.Actions);
        Assert.Single(_queue.Enqueued);
    }

    [Fact]
    public async Task Request_ActionNotAllowedForTemplate_Rejects()
    {
        // web-siteテンプレートはコンテナ再起動を許可していない
        AddTarget(templateId: "web-site");
        AddIncident();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
            {
                ActionId = RecoveryActionCatalog.RestartAllowedContainer,
                TargetResource = "web",
            }, idempotencyKey: "key-1"));

        Assert.Equal("action_not_allowed_for_target", ex.Code);
    }

    [Fact]
    public async Task Request_MediumWithoutApproval_Rejects()
    {
        AddTarget();
        AddIncident();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
            {
                ActionId = RecoveryActionCatalog.StopAllowedContainer,
                TargetResource = "web",
            }, idempotencyKey: "key-1"));

        Assert.Equal("approval_required", ex.Code);
    }

    [Fact]
    public async Task Request_MediumWithValidApproval_Queues()
    {
        AddTarget();
        AddIncident();
        var approval = AddApproval();

        var dto = await CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
        {
            ActionId = RecoveryActionCatalog.StopAllowedContainer,
            TargetResource = "web",
            ApprovalId = approval.Id,
        }, idempotencyKey: "key-1");

        Assert.Equal("Queued", dto.Status);
        Assert.Equal("Medium", dto.RiskLevel);
        Assert.Equal(approval.Id, dto.ApprovalId);
    }

    [Fact]
    public async Task Request_ApprovalForDifferentResource_Rejects()
    {
        AddTarget();
        AddIncident();
        var approval = AddApproval(resource: "other-container");

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
            {
                ActionId = RecoveryActionCatalog.StopAllowedContainer,
                TargetResource = "web",
                ApprovalId = approval.Id,
            }, idempotencyKey: "key-1"));

        Assert.Equal("approval_mismatch", ex.Code);
    }

    [Fact]
    public async Task Request_ApprovalForDifferentAction_Rejects()
    {
        AddTarget();
        AddIncident();
        var approval = AddApproval(actionId: RecoveryActionCatalog.StartAllowedContainer);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
            {
                ActionId = RecoveryActionCatalog.StopAllowedContainer,
                TargetResource = "web",
                ApprovalId = approval.Id,
            }, idempotencyKey: "key-1"));

        Assert.Equal("approval_mismatch", ex.Code);
    }

    [Fact]
    public async Task Request_RejectedApproval_Rejects()
    {
        AddTarget();
        AddIncident();
        var approval = AddApproval(status: ApprovalStatus.Rejected);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
            {
                ActionId = RecoveryActionCatalog.StopAllowedContainer,
                TargetResource = "web",
                ApprovalId = approval.Id,
            }, idempotencyKey: "key-1"));

        Assert.Equal("approval_not_usable", ex.Code);
    }

    [Fact]
    public async Task Request_ConsumedApproval_Rejects()
    {
        AddTarget();
        AddIncident();
        var approval = AddApproval(consumed: true);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
            {
                ActionId = RecoveryActionCatalog.StopAllowedContainer,
                TargetResource = "web",
                ApprovalId = approval.Id,
            }, idempotencyKey: "key-1"));

        Assert.Equal("approval_not_usable", ex.Code);
    }

    [Fact]
    public async Task Request_ExpiredApproval_Rejects()
    {
        AddTarget();
        AddIncident();
        var approval = AddApproval();
        _time.Now = BaseTime.AddHours(2);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().RequestActionAsync(1, new CreateRecoveryActionRequest
            {
                ActionId = RecoveryActionCatalog.StopAllowedContainer,
                TargetResource = "web",
                ApprovalId = approval.Id,
            }, idempotencyKey: "key-1"));

        Assert.Equal("approval_not_usable", ex.Code);
    }

    [Fact]
    public async Task Request_WithinCooldown_IsBlocked()
    {
        AddTarget();
        AddIncident();
        var sut = CreateSut();

        await sut.RequestActionAsync(1, new CreateRecoveryActionRequest
        {
            ActionId = RecoveryActionCatalog.RestartAllowedContainer,
            TargetResource = "web",
        }, idempotencyKey: "key-1");

        _time.Now = BaseTime.AddMinutes(5);
        var second = await sut.RequestActionAsync(1, new CreateRecoveryActionRequest
        {
            ActionId = RecoveryActionCatalog.RestartAllowedContainer,
            TargetResource = "web",
        }, idempotencyKey: "key-2");

        Assert.Equal("Blocked", second.Status);
        Assert.Equal("cooldown", second.BlockedReason);
        Assert.Single(_queue.Enqueued);
        Assert.Contains(_audit.Entries, e => e.Action == "recovery.action.blocked");
    }

    [Fact]
    public async Task Request_ExceedingHourlyLimit_IsBlocked()
    {
        AddTarget();
        AddIncident();
        var sut = CreateSut();

        // クールダウンを超えつつ1時間以内に3回目を試みる(上限2回)
        await sut.RequestActionAsync(1, Restart("web"), "key-1");
        _time.Now = BaseTime.AddMinutes(15);
        await sut.RequestActionAsync(1, Restart("web"), "key-2");
        _time.Now = BaseTime.AddMinutes(30);
        var third = await sut.RequestActionAsync(1, Restart("web"), "key-3");

        Assert.Equal("Blocked", third.Status);
        Assert.Equal("rate_limit", third.BlockedReason);
        Assert.Equal(2, _queue.Enqueued.Count);
    }

    [Fact]
    public async Task Request_AfterConsecutiveFailures_CircuitOpens()
    {
        AddTarget();
        AddIncident();
        // 直近3回が連続失敗の履歴(最後の失敗は30分前 = 遮断時間60分の範囲内)
        SeedPastActions(
            (BaseTime.AddHours(-3), RecoveryActionStatus.Failed),
            (BaseTime.AddHours(-2), RecoveryActionStatus.Failed),
            (BaseTime.AddMinutes(-30), RecoveryActionStatus.Failed));

        var blocked = await CreateSut().RequestActionAsync(1, Restart("web"), "key-final");

        Assert.Equal("Blocked", blocked.Status);
        Assert.Equal("circuit_open", blocked.BlockedReason);
    }

    [Fact]
    public async Task Request_AfterCircuitOpenPeriodElapsed_IsAllowedAgain()
    {
        AddTarget();
        AddIncident();
        // 最後の失敗から遮断時間(60分)以上経過している
        SeedPastActions(
            (BaseTime.AddHours(-5), RecoveryActionStatus.Failed),
            (BaseTime.AddHours(-4), RecoveryActionStatus.Failed),
            (BaseTime.AddHours(-3), RecoveryActionStatus.Failed));

        var dto = await CreateSut().RequestActionAsync(1, Restart("web"), "key-final");

        Assert.Equal("Queued", dto.Status);
    }

    [Fact]
    public async Task Request_SuccessBreaksConsecutiveFailures()
    {
        AddTarget();
        AddIncident();
        // 失敗2回のあと成功しているため連続失敗は1回にリセットされる
        SeedPastActions(
            (BaseTime.AddHours(-4), RecoveryActionStatus.Failed),
            (BaseTime.AddHours(-3), RecoveryActionStatus.Failed),
            (BaseTime.AddHours(-2), RecoveryActionStatus.Succeeded),
            (BaseTime.AddMinutes(-30), RecoveryActionStatus.Failed));

        var dto = await CreateSut().RequestActionAsync(1, Restart("web"), "key-final");

        Assert.Equal("Queued", dto.Status);
    }

    /// <summary>過去の実行履歴を直接投入する(レート制限の判定条件を検証するため)。</summary>
    private void SeedPastActions(params (DateTimeOffset At, RecoveryActionStatus Status)[] history)
    {
        foreach (var (at, status) in history)
        {
            _actions.Actions.Add(new RecoveryAction
            {
                Id = _actions.Actions.Count + 1,
                IncidentId = 1,
                TargetId = 1,
                ActionId = RecoveryActionCatalog.RestartAllowedContainer,
                TargetResource = "web",
                RiskLevel = ActionRiskLevel.Low,
                Status = status,
                RequestedAt = at.UtcDateTime,
                CompletedAt = at.UtcDateTime,
            });
        }
    }

    [Fact]
    public async Task Request_RecheckHttpHealth_IsNotRateLimited()
    {
        AddTarget(templateId: "web-site");
        AddIncident();
        var sut = CreateSut();

        var first = await sut.RequestActionAsync(1, new CreateRecoveryActionRequest
        {
            ActionId = RecoveryActionCatalog.RecheckHttpHealth,
        }, idempotencyKey: null);

        _time.Now = BaseTime.AddMinutes(1);
        var second = await sut.RequestActionAsync(1, new CreateRecoveryActionRequest
        {
            ActionId = RecoveryActionCatalog.RecheckHttpHealth,
        }, idempotencyKey: null);

        Assert.Equal("Queued", first.Status);
        Assert.Equal("Queued", second.Status);
    }

    [Fact]
    public async Task CreateApproval_ForLowAction_Rejects()
    {
        AddTarget();
        AddIncident();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().CreateApprovalAsync(1, new CreateApprovalRequest
            {
                ActionId = RecoveryActionCatalog.RestartAllowedContainer,
                TargetResource = "web",
            }));

        Assert.Equal("approval_not_required", ex.Code);
    }

    [Fact]
    public async Task CreateApproval_Approve_SetsExpiryAndAudits()
    {
        AddTarget();
        AddIncident();

        var dto = await CreateSut().CreateApprovalAsync(1, new CreateApprovalRequest
        {
            ActionId = RecoveryActionCatalog.StopAllowedContainer,
            TargetResource = "web",
            Approve = true,
        });

        Assert.Equal("Approved", dto.Status);
        Assert.Equal(BaseTime.UtcDateTime.AddMinutes(30), dto.ExpiresAt);
        Assert.False(dto.IsConsumed);
        Assert.Contains(_audit.Entries, e => e.Action == "recovery.approval.approved");
    }

    [Fact]
    public async Task CreateApproval_Reject_IsAuditedAsDenied()
    {
        AddTarget();
        AddIncident();

        var dto = await CreateSut().CreateApprovalAsync(1, new CreateApprovalRequest
        {
            ActionId = RecoveryActionCatalog.StopAllowedContainer,
            TargetResource = "web",
            Approve = false,
            Comment = "影響範囲が不明なため却下",
        });

        Assert.Equal("Rejected", dto.Status);
        Assert.Contains(_audit.Entries,
            e => e.Action == "recovery.approval.rejected" && e.Result == Core.Models.Auth.AuditResult.Denied);
    }

    [Fact]
    public async Task RunHealthCheck_RecordsAudit()
    {
        AddTarget();

        var dto = await CreateSut().RunHealthCheckAsync(1);

        Assert.Equal("Healthy", dto.Status);
        Assert.Contains(_audit.Entries, e => e.Action == "target.health_check");
    }

    [Fact]
    public async Task RunHealthCheck_UnknownTarget_Throws()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().RunHealthCheckAsync(999));
        Assert.Equal("target_not_found", ex.Code);
    }

    private static CreateRecoveryActionRequest Restart(string resource) => new()
    {
        ActionId = RecoveryActionCatalog.RestartAllowedContainer,
        TargetResource = resource,
    };
}
