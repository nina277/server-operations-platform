using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

public interface IRecoveryExecutionService
{
    /// <summary>
    /// キュー済みの復旧アクションを実行する。Workerからのみ呼び出す。
    /// 実行前に許可リスト・対象能力・承認状態を再検証する。
    /// </summary>
    Task ExecuteAsync(long recoveryActionId, CancellationToken ct = default);
}

public interface IHealthCheckService
{
    /// <summary>対象のヘルスチェックを実行して結果を保存する。</summary>
    Task<HealthCheck> RunAsync(
        long targetId, long? recoveryActionId = null, CancellationToken ct = default);
}

/// <summary>
/// 復旧アクションの実行。Workerだけがこのサービスを通じて許可済みアクションを実行する。
/// </summary>
public class RecoveryExecutionService(
    IRecoveryActionRepository actions,
    IRecoveryApprovalRepository approvals,
    IMonitoringTargetRepository targets,
    IAdapterTemplateCatalog templates,
    IRecoveryActionCatalog actionCatalog,
    IDockerAdapter dockerAdapter,
    IHealthCheckService healthCheckService,
    TimeProvider timeProvider,
    ILogger<RecoveryExecutionService> logger) : IRecoveryExecutionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(long recoveryActionId, CancellationToken ct = default)
    {
        var action = await actions.FindByIdAsync(recoveryActionId, ct);
        if (action is null)
        {
            logger.LogWarning("Recovery action {ActionId} not found.", recoveryActionId);
            return;
        }

        if (action.Status != RecoveryActionStatus.Queued)
        {
            logger.LogInformation(
                "Recovery action {ActionId} is not queued (status={Status}); skipping.",
                recoveryActionId, action.Status);
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        action.Status = RecoveryActionStatus.Running;
        action.StartedAt = now;
        await actions.SaveChangesAsync(ct);

        try
        {
            var result = await ExecuteCoreAsync(action, ct);

            action.Status = result.Success ? RecoveryActionStatus.Succeeded : RecoveryActionStatus.Failed;
            action.ResultMessage = result.Message;
            action.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
            await actions.SaveChangesAsync(ct);

            // 承認は1回の実行で消費する(再利用を防ぐ)
            if (action.ApprovalId is { } approvalId)
            {
                var approval = await approvals.FindByIdAsync(approvalId, ct);
                if (approval is not null && !approval.IsConsumed)
                {
                    approval.IsConsumed = true;
                    await approvals.SaveChangesAsync(ct);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Recovery action {ActionId} threw an exception.", recoveryActionId);
            action.Status = RecoveryActionStatus.Failed;
            action.ResultMessage = "復旧処理中にエラーが発生しました。";
            action.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
            await actions.SaveChangesAsync(ct);
        }

        // 復旧後のヘルスチェックは成否にかかわらず実行し、現状を記録する
        try
        {
            await healthCheckService.RunAsync(action.TargetId, action.Id, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Post-recovery health check failed for action {ActionId}.", recoveryActionId);
        }
    }

    private async Task<AdapterConnectionResult> ExecuteCoreAsync(RecoveryAction action, CancellationToken ct)
    {
        var definition = actionCatalog.Find(action.ActionId);
        if (definition is null || definition.RiskLevel == ActionRiskLevel.High)
        {
            return new AdapterConnectionResult(false, "許可されていない操作です。");
        }

        var target = await targets.FindByIdAsync(action.TargetId, ct);
        if (target is null || !target.IsEnabled)
        {
            return new AdapterConnectionResult(false, "対象が存在しないか無効です。");
        }

        // 実行直前にも対象テンプレートの許可操作を再検証する
        var template = templates.Find(target.TemplateId);
        if (template is null || !template.AllowedOperations.Contains(action.ActionId))
        {
            return new AdapterConnectionResult(false, "この対象では許可されていない操作です。");
        }

        // コンテナ許可リストも実行直前に再検証する(受付後に設定が変わった場合に備える)
        if (definition.RequiresTargetResource && !AllowedContainers.IsAllowed(target, action.TargetResource))
        {
            return new AdapterConnectionResult(
                false, "このコンテナは操作許可リストに登録されていません。");
        }

        // Medium操作は有効な承認が必須
        if (definition.RequiresApproval)
        {
            if (action.ApprovalId is not { } approvalId)
            {
                return new AdapterConnectionResult(false, "承認がありません。");
            }

            var approval = await approvals.FindByIdAsync(approvalId, ct);
            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (approval is null || !approval.IsUsable(now)
                || approval.ActionId != action.ActionId
                || approval.TargetResource != action.TargetResource
                || approval.IncidentId != action.IncidentId)
            {
                return new AdapterConnectionResult(false, "有効な承認がありません(期限切れ・使用済み・不一致)。");
            }
        }

        if (action.ActionId == RecoveryActionCatalog.RecheckHttpHealth)
        {
            var check = await healthCheckService.RunAsync(action.TargetId, action.Id, ct);
            return new AdapterConnectionResult(
                check.Status == HealthCheckResultStatus.Healthy, check.Message, check.LatencyMs);
        }

        var settings = target.Profile is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(target.Profile.SettingsJson, JsonOptions) ?? [];
        if (!settings.TryGetValue("endpoint", out var endpoint))
        {
            return new AdapterConnectionResult(false, "対象のDockerエンドポイントが設定されていません。");
        }

        if (string.IsNullOrWhiteSpace(action.TargetResource))
        {
            return new AdapterConnectionResult(false, "操作対象のコンテナが指定されていません。");
        }

        var operation = action.ActionId switch
        {
            RecoveryActionCatalog.RestartAllowedContainer => ContainerOperation.Restart,
            RecoveryActionCatalog.StartAllowedContainer => ContainerOperation.Start,
            RecoveryActionCatalog.StopAllowedContainer => ContainerOperation.Stop,
            _ => (ContainerOperation?)null,
        };
        if (operation is null)
        {
            return new AdapterConnectionResult(false, "許可されていない操作です。");
        }

        return await dockerAdapter.ControlContainerAsync(endpoint, action.TargetResource, operation.Value, ct);
    }
}
