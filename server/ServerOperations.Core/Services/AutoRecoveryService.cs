using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

public interface IAutoRecoveryService
{
    /// <summary>
    /// 診断結果に基づく自動復旧を試みる。実行した場合はそのアクションを返し、
    /// 条件を満たさず実行しなかった場合はnullを返す。
    /// </summary>
    Task<RecoveryAction?> TryRecoverAsync(
        MonitoringTarget target, Incident incident, Diagnosis diagnosis, CancellationToken ct = default);
}

/// <summary>
/// ルール診断からの自動復旧。指示書6.2に従い、次をすべて満たす場合のみ実行する。
///
/// 1. 対象で自動復旧が有効(初期値OFF)
/// 2. 診断が推奨アクションを持ち、対象能力で許可されている
/// 3. アクションがLow危険度かつ承認不要(Medium以上は自動実行しない)
/// 4. 操作対象コンテナが対象別の許可リストに含まれる
/// 5. クールダウン・回数上限・サーキットブレーカーを通過する
///
/// 実行は既存のRecoveryExecutionServiceを通し、実行直前の再検証と
/// 復旧後ヘルスチェックを共通の経路で行う。
/// </summary>
public class AutoRecoveryService(
    IRecoveryActionRepository actions,
    IRecoveryActionCatalog catalog,
    IRecoveryRateLimiter rateLimiter,
    IRecoveryExecutionService executionService,
    IAuditLogRepository auditLogs,
    Notifications.INotificationService notificationService,
    IMaintenanceService maintenance,
    TimeProvider timeProvider,
    ILogger<AutoRecoveryService> logger) : IAutoRecoveryService
{
    public async Task<RecoveryAction?> TryRecoverAsync(
        MonitoringTarget target, Incident incident, Diagnosis diagnosis, CancellationToken ct = default)
    {
        // 1. 対象で自動復旧が有効か
        if (!target.AutoRecoveryEnabled)
        {
            return null;
        }

        // 計画停止中は勝手に直さない。停止作業そのものを障害と見て
        // 復旧をかけると、作業を邪魔することになる。
        var maintenanceState = await maintenance.GetStateAsync(target.Id, ct);
        if (maintenanceState.SuppressAutoRecovery)
        {
            await RecordDeniedAsync(
                incident, diagnosis.RecommendedActionId ?? "-",
                $"メンテナンス期間中のため自動実行しません: {maintenanceState.Reason}", ct);
            return null;
        }

        // 2. 診断が実行可能な推奨アクションを持つか
        if (diagnosis.RecommendedActionId is not { } actionId || !diagnosis.RecommendedActionAllowed)
        {
            return null;
        }

        var definition = catalog.Find(actionId);
        if (definition is null)
        {
            return null;
        }

        // 3. Low危険度かつ承認不要のものだけを自動実行する
        if (definition.RiskLevel != ActionRiskLevel.Low || definition.RequiresApproval)
        {
            await RecordDeniedAsync(
                incident, actionId,
                $"自動実行の対象外です(危険度={definition.RiskLevel}, 承認要否={definition.RequiresApproval})。",
                ct);
            return null;
        }

        // 副作用のない再チェックは自動復旧の対象にしない(収集で同等の確認が行われるため)
        if (actionId == RecoveryActionCatalog.RecheckHttpHealth)
        {
            return null;
        }

        // 4. 操作対象コンテナが許可リストに含まれるか
        var containerName = incident.Service;
        if (definition.RequiresTargetResource && !AllowedContainers.IsAllowed(target, containerName))
        {
            await RecordDeniedAsync(
                incident, actionId,
                $"コンテナが操作許可リストに含まれていません: {containerName}", ct);
            return null;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var action = new RecoveryAction
        {
            IncidentId = incident.Id,
            TargetId = target.Id,
            ActionId = actionId,
            TargetResource = containerName,
            RiskLevel = definition.RiskLevel,
            // 自動実行は要求者がいないためnull。冪等キーはインシデントと発生回数から決定的に作る。
            RequestedByUserId = null,
            IdempotencyKey = $"auto-{incident.Id}-{incident.OccurrenceCount}",
            RequestedAt = now,
            Status = RecoveryActionStatus.Queued,
        };

        // 同一キーの重複実行を防ぐ(同じ発生回数で二重に走らせない)
        var existing = await actions.FindByIdempotencyKeyAsync(action.IdempotencyKey, ct);
        if (existing is not null)
        {
            return null;
        }

        // 5. クールダウン・回数上限・サーキットブレーカー
        var decision = await rateLimiter.CheckAsync(target.Id, actionId, containerName, ct);
        if (!decision.Allowed)
        {
            action.Status = RecoveryActionStatus.Blocked;
            action.BlockedReason = decision.Reason;
            action.ResultMessage = decision.Message;
            action.CompletedAt = now;

            await actions.AddAsync(action, ct);
            await actions.SaveChangesAsync(ct);

            await RecordAuditAsync(
                "recovery.auto.blocked", action.Id.ToString(), AuditResult.Denied,
                $"incident={incident.Id} action={actionId} reason={decision.Reason}", ct);

            return action;
        }

        await actions.AddAsync(action, ct);
        await actions.SaveChangesAsync(ct);

        await RecordAuditAsync(
            "recovery.auto.requested", action.Id.ToString(), AuditResult.Success,
            $"incident={incident.Id} action={actionId} resource={containerName} "
                + $"diagnosis={diagnosis.Id} source={diagnosis.Source}",
            ct);

        logger.LogInformation(
            "Auto recovery triggered for incident {IncidentId}: {ActionId} on {Resource}",
            incident.Id, actionId, containerName);

        // 実行は共通経路を通す(実行直前の再検証と復旧後ヘルスチェックが行われる)
        await executionService.ExecuteAsync(action.Id, ct);

        await NotifyResultAsync(incident, action, ct);
        return action;
    }

    /// <summary>
    /// 自動復旧の結果を通知する。人手を介さず実行されるため、成否にかかわらず知らせる。
    /// 本文にはログ全文・秘密情報を含めない。
    /// </summary>
    private async Task NotifyResultAsync(Incident incident, RecoveryAction action, CancellationToken ct)
    {
        try
        {
            var succeeded = action.Status == RecoveryActionStatus.Succeeded;

            await notificationService.NotifyAsync(new Notifications.NotificationRequest
            {
                // 失敗は人手の対応が必要なため重大度を上げる
                Severity = succeeded
                    ? Models.Operations.NotificationSeverity.Medium
                    : Models.Operations.NotificationSeverity.High,
                Title = succeeded
                    ? $"自動復旧を実行しました: {action.TargetResource}"
                    : $"自動復旧に失敗しました: {action.TargetResource}",
                Body = $"操作: {action.ActionId} / 対象: {action.TargetResource}"
                    + $" / 結果: {action.ResultMessage ?? action.Status.ToString()}",
                // インシデントの通知とは別枠で集約する
                AggregationKey = $"auto-recovery-{incident.SignatureSha256}",
                IncidentId = incident.Id,
                TargetId = action.TargetId,
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to notify auto recovery result for action {ActionId}.", action.Id);
        }
    }

    private Task RecordDeniedAsync(Incident incident, string actionId, string reason, CancellationToken ct) =>
        RecordAuditAsync(
            "recovery.auto.denied", incident.Id.ToString(), AuditResult.Denied,
            $"incident={incident.Id} action={actionId} reason={reason}", ct);

    /// <summary>
    /// 自動実行の監査。操作者は人ではないため system として記録する。
    /// </summary>
    private async Task RecordAuditAsync(
        string action, string? targetId, AuditResult result, string details, CancellationToken ct)
    {
        try
        {
            await auditLogs.AddAsync(new AuditLog
            {
                OccurredAt = timeProvider.GetUtcNow().UtcDateTime,
                ActorUserId = null,
                ActorName = "system(auto-recovery)",
                IpAddress = "internal",
                UserAgent = "server-operations-worker",
                TargetType = "RecoveryAction",
                TargetId = targetId,
                Action = action,
                Result = result,
                Details = details,
                TraceId = Activity.Current?.Id,
            }, ct);
            await auditLogs.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to record auto recovery audit log. action={Action}", action);
        }
    }
}
