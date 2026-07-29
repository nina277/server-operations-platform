using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>
/// 復旧の受付。actionId・targetId・approvalId・利用者・インシデント対象の関係をService層で検証し、
/// 通過した要求だけをWorkerのキューへ積む。High操作を実行する経路は存在しない。
/// </summary>
public class RecoveryService(
    IIncidentRepository incidents,
    IMonitoringTargetRepository targets,
    IRecoveryActionRepository actions,
    IRecoveryApprovalRepository approvals,
    IHealthCheckRepository healthChecks,
    IAdapterTemplateCatalog templates,
    IRecoveryActionCatalog actionCatalog,
    IRecoveryRateLimiter rateLimiter,
    IRecoveryJobQueue jobQueue,
    IHealthCheckService healthCheckService,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    RecoveryLimits limits,
    TimeProvider timeProvider) : IRecoveryService
{
    public async Task<ApprovalDto> CreateApprovalAsync(
        long incidentId, CreateApprovalRequest request, CancellationToken ct = default)
    {
        var incident = await FindIncidentOrThrowAsync(incidentId, ct);
        var definition = FindDefinitionOrThrow(request.ActionId);

        if (definition.RiskLevel == ActionRiskLevel.High)
        {
            throw AppException.Forbidden("high_risk_not_allowed", "この操作は実行できません。");
        }

        if (!definition.RequiresApproval)
        {
            throw AppException.BadRequest(
                "approval_not_required", "この操作に承認は不要です。");
        }

        if (definition.RequiresTargetResource && string.IsNullOrWhiteSpace(request.TargetResource))
        {
            throw AppException.BadRequest("target_resource_required", "操作対象を指定してください。");
        }

        // 承認時点でも対象テンプレートの許可操作を検証する
        await EnsureActionAllowedForTargetAsync(incident.TargetId, definition.ActionId, ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var approval = new RecoveryApproval
        {
            IncidentId = incident.Id,
            TargetId = incident.TargetId,
            ActionId = definition.ActionId,
            TargetResource = request.TargetResource,
            Status = request.Approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected,
            DecidedByUserId = currentUser.UserId,
            DecidedByUsername = currentUser.Username,
            DecidedAt = now,
            Comment = request.Comment,
            ExpiresAt = now.AddMinutes(limits.ApprovalExpiryMinutes),
            CreatedAt = now,
        };

        await approvals.AddAsync(approval, ct);
        await approvals.SaveChangesAsync(ct);

        await audit.RecordAsync(
            request.Approve ? "recovery.approval.approved" : "recovery.approval.rejected",
            "RecoveryApproval", approval.Id.ToString(),
            request.Approve ? AuditResult.Success : AuditResult.Denied,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"incident={incident.Id} action={definition.ActionId} resource={request.TargetResource}",
            ct: ct);

        return ApprovalDto.From(approval);
    }

    public async Task<List<ApprovalDto>> GetApprovalsAsync(long incidentId, CancellationToken ct = default)
    {
        await FindIncidentOrThrowAsync(incidentId, ct);
        var items = await approvals.GetByIncidentAsync(incidentId, ct);
        return items.Select(ApprovalDto.From).ToList();
    }

    public async Task<RecoveryActionDto> RequestActionAsync(
        long incidentId, CreateRecoveryActionRequest request, string? idempotencyKey,
        CancellationToken ct = default)
    {
        var incident = await FindIncidentOrThrowAsync(incidentId, ct);
        var definition = FindDefinitionOrThrow(request.ActionId);

        // High操作はカタログに存在しないが、多層防御として明示的に拒否する
        if (definition.RiskLevel == ActionRiskLevel.High)
        {
            await audit.RecordAsync(
                "recovery.action.denied", "RecoveryAction", null, AuditResult.Denied,
                actorUserId: currentUser.UserId, actorName: currentUser.Username,
                details: $"high risk action rejected: {request.ActionId}", ct: ct);
            throw AppException.Forbidden("high_risk_not_allowed", "この操作は実行できません。");
        }

        if (definition.RequiresIdempotencyKey && string.IsNullOrWhiteSpace(idempotencyKey))
        {
            throw AppException.BadRequest(
                "idempotency_key_required", "この操作にはIdempotency-Keyヘッダーが必要です。");
        }

        // 冪等性: 同一キーの再送では新規実行せず、既存の結果を返す
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var existing = await actions.FindByIdempotencyKeyAsync(idempotencyKey, ct);
            if (existing is not null)
            {
                return RecoveryActionDto.From(existing);
            }
        }

        if (definition.RequiresTargetResource && string.IsNullOrWhiteSpace(request.TargetResource))
        {
            throw AppException.BadRequest("target_resource_required", "操作対象を指定してください。");
        }

        await EnsureActionAllowedForTargetAsync(incident.TargetId, definition.ActionId, ct);

        // Medium操作の承認検証: 承認とインシデント・アクション・対象リソースの一致を確認する
        RecoveryApproval? approval = null;
        if (definition.RequiresApproval)
        {
            if (request.ApprovalId is not { } approvalId)
            {
                throw AppException.BadRequest("approval_required", "この操作には管理者の承認が必要です。");
            }

            approval = await approvals.FindByIdAsync(approvalId, ct)
                ?? throw AppException.NotFound("approval_not_found", "承認が見つかりません。");

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (approval.IncidentId != incident.Id
                || approval.ActionId != definition.ActionId
                || approval.TargetResource != request.TargetResource)
            {
                throw AppException.BadRequest(
                    "approval_mismatch", "承認の内容が要求と一致しません。");
            }

            if (!approval.IsUsable(now))
            {
                var reason = approval.Status != ApprovalStatus.Approved ? "承認されていません"
                    : approval.IsConsumed ? "既に使用済みです"
                    : "有効期限が切れています";
                throw AppException.BadRequest("approval_not_usable", $"この承認は使用できません({reason})。");
            }
        }

        var requestedAt = timeProvider.GetUtcNow().UtcDateTime;
        var action = new RecoveryAction
        {
            IncidentId = incident.Id,
            TargetId = incident.TargetId,
            ActionId = definition.ActionId,
            TargetResource = request.TargetResource,
            RiskLevel = definition.RiskLevel,
            ApprovalId = approval?.Id,
            IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : idempotencyKey,
            RequestedByUserId = currentUser.UserId,
            RequestedAt = requestedAt,
            Status = RecoveryActionStatus.Queued,
        };

        // クールダウン・回数上限・サーキットブレーカー
        var decision = await rateLimiter.CheckAsync(
            incident.TargetId, definition.ActionId, request.TargetResource, ct);
        if (!decision.Allowed)
        {
            action.Status = RecoveryActionStatus.Blocked;
            action.BlockedReason = decision.Reason;
            action.ResultMessage = decision.Message;
            action.CompletedAt = requestedAt;

            await actions.AddAsync(action, ct);
            await actions.SaveChangesAsync(ct);

            await audit.RecordAsync(
                "recovery.action.blocked", "RecoveryAction", action.Id.ToString(), AuditResult.Denied,
                actorUserId: currentUser.UserId, actorName: currentUser.Username,
                details: $"incident={incident.Id} action={definition.ActionId} reason={decision.Reason}",
                ct: ct);

            return RecoveryActionDto.From(action);
        }

        await actions.AddAsync(action, ct);
        await actions.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "recovery.action.requested", "RecoveryAction", action.Id.ToString(), AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"incident={incident.Id} action={definition.ActionId} resource={request.TargetResource} risk={definition.RiskLevel}",
            ct: ct);

        // 実行はWorkerが行う(APIプロセスでは実行しない)
        jobQueue.Enqueue(action.Id);

        return RecoveryActionDto.From(action);
    }

    public async Task<List<RecoveryActionDto>> GetActionsAsync(long incidentId, CancellationToken ct = default)
    {
        await FindIncidentOrThrowAsync(incidentId, ct);
        var items = await actions.GetByIncidentAsync(incidentId, ct);
        return items.Select(RecoveryActionDto.From).ToList();
    }

    public async Task<HealthCheckDto> RunHealthCheckAsync(long targetId, CancellationToken ct = default)
    {
        _ = await targets.FindByIdAsync(targetId, ct)
            ?? throw AppException.NotFound("target_not_found", "監視対象が見つかりません。");

        var check = await healthCheckService.RunAsync(targetId, recoveryActionId: null, ct);

        await audit.RecordAsync(
            "target.health_check", "MonitoringTarget", targetId.ToString(),
            check.Status == HealthCheckResultStatus.Healthy ? AuditResult.Success : AuditResult.Failure,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: check.Message, ct: ct);

        return HealthCheckDto.From(check);
    }

    private async Task<Incident> FindIncidentOrThrowAsync(long incidentId, CancellationToken ct) =>
        await incidents.FindByIdAsync(incidentId, ct)
            ?? throw AppException.NotFound("incident_not_found", "インシデントが見つかりません。");

    private RecoveryActionDefinition FindDefinitionOrThrow(string actionId) =>
        actionCatalog.Find(actionId)
            ?? throw AppException.BadRequest("unknown_action", "不明な復旧アクションです。");

    /// <summary>対象テンプレートがそのアクションを許可しているか検証する。</summary>
    private async Task EnsureActionAllowedForTargetAsync(long targetId, string actionId, CancellationToken ct)
    {
        var target = await targets.FindByIdAsync(targetId, ct)
            ?? throw AppException.NotFound("target_not_found", "監視対象が見つかりません。");

        var template = templates.Find(target.TemplateId);
        if (template is null || !template.AllowedOperations.Contains(actionId))
        {
            throw AppException.Forbidden(
                "action_not_allowed_for_target", "この対象では許可されていない操作です。");
        }
    }
}
