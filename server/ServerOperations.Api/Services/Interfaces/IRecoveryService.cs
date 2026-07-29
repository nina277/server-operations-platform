using ServerOperations.Api.DTOs.Operations;

namespace ServerOperations.Api.Services.Interfaces;

public interface IRecoveryService
{
    Task<ApprovalDto> CreateApprovalAsync(
        long incidentId, CreateApprovalRequest request, CancellationToken ct = default);

    Task<List<ApprovalDto>> GetApprovalsAsync(long incidentId, CancellationToken ct = default);

    /// <summary>
    /// 復旧アクションを要求する。許可リスト・対象能力・承認・冪等性・レート制限を検証し、
    /// 通過した場合のみキューへ積む(実行はWorker)。
    /// </summary>
    Task<RecoveryActionDto> RequestActionAsync(
        long incidentId, CreateRecoveryActionRequest request, string? idempotencyKey,
        CancellationToken ct = default);

    Task<List<RecoveryActionDto>> GetActionsAsync(long incidentId, CancellationToken ct = default);

    Task<HealthCheckDto> RunHealthCheckAsync(long targetId, CancellationToken ct = default);
}

/// <summary>復旧実行をWorkerへ引き渡すキュー。API層はHangfireへ直接依存しない。</summary>
public interface IRecoveryJobQueue
{
    void Enqueue(long recoveryActionId);
}
