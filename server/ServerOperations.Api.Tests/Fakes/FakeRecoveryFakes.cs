using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests.Fakes;

public class FakeRecoveryActionRepository : IRecoveryActionRepository
{
    public List<RecoveryAction> Actions { get; } = [];

    public Task<RecoveryAction?> FindByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Actions.FirstOrDefault(a => a.Id == id));

    public Task<RecoveryAction?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Actions.FirstOrDefault(a => a.IdempotencyKey == key));

    public Task<List<RecoveryAction>> GetByIncidentAsync(long incidentId, CancellationToken ct = default) =>
        Task.FromResult(Actions
            .Where(a => a.IncidentId == incidentId)
            .OrderByDescending(a => a.RequestedAt)
            .ToList());

    public Task<List<RecoveryAction>> GetRecentForRateLimitAsync(
        long targetId, string actionId, string? targetResource, DateTime sinceUtc, CancellationToken ct = default) =>
        Task.FromResult(Actions
            .Where(a => a.TargetId == targetId
                && a.ActionId == actionId
                && a.TargetResource == targetResource
                && a.RequestedAt >= sinceUtc)
            .OrderByDescending(a => a.RequestedAt)
            .ToList());

    public Task AddAsync(RecoveryAction action, CancellationToken ct = default)
    {
        action.Id = Actions.Count + 1;
        Actions.Add(action);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeRecoveryApprovalRepository : IRecoveryApprovalRepository
{
    public List<RecoveryApproval> Approvals { get; } = [];

    public Task<RecoveryApproval?> FindByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Approvals.FirstOrDefault(a => a.Id == id));

    public Task<List<RecoveryApproval>> GetByIncidentAsync(long incidentId, CancellationToken ct = default) =>
        Task.FromResult(Approvals.Where(a => a.IncidentId == incidentId).ToList());

    public Task AddAsync(RecoveryApproval approval, CancellationToken ct = default)
    {
        approval.Id = Approvals.Count + 1;
        Approvals.Add(approval);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeHealthCheckRepository : IHealthCheckRepository
{
    public List<HealthCheck> Checks { get; } = [];

    public Task AddAsync(HealthCheck check, CancellationToken ct = default)
    {
        check.Id = Checks.Count + 1;
        Checks.Add(check);
        return Task.CompletedTask;
    }

    public Task<List<HealthCheck>> GetRecentAsync(long targetId, int limit, CancellationToken ct = default) =>
        Task.FromResult(Checks
            .Where(h => h.TargetId == targetId)
            .OrderByDescending(h => h.CheckedAt)
            .Take(limit)
            .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeRecoveryJobQueue : IRecoveryJobQueue
{
    public List<long> Enqueued { get; } = [];

    public void Enqueue(long recoveryActionId) => Enqueued.Add(recoveryActionId);
}

public class FakeHealthCheckService : IHealthCheckService
{
    public HealthCheckResultStatus ResultStatus { get; set; } = HealthCheckResultStatus.Healthy;

    public string Message { get; set; } = "正常です。";

    public List<(long TargetId, long? RecoveryActionId)> Calls { get; } = [];

    public Task<HealthCheck> RunAsync(
        long targetId, long? recoveryActionId = null, CancellationToken ct = default)
    {
        Calls.Add((targetId, recoveryActionId));
        return Task.FromResult(new HealthCheck
        {
            Id = Calls.Count,
            TargetId = targetId,
            RecoveryActionId = recoveryActionId,
            Status = ResultStatus,
            Message = Message,
            CheckedAt = DateTime.UtcNow,
        });
    }
}

/// <summary>
/// 復旧の実行を記録するだけのFake。実際にはコンテナを操作しない。
/// </summary>
public class RecordingExecutionService : IRecoveryExecutionService
{
    public List<long> Executed { get; } = [];

    /// <summary>実行後に設定する状態(通知内容の検証用)。</summary>
    public RecoveryActionStatus ResultStatus { get; set; } = RecoveryActionStatus.Succeeded;

    public FakeRecoveryActionRepository? Actions { get; set; }

    public Task ExecuteAsync(long recoveryActionId, CancellationToken ct = default)
    {
        Executed.Add(recoveryActionId);

        var action = Actions?.Actions.FirstOrDefault(a => a.Id == recoveryActionId);
        if (action is not null)
        {
            action.Status = ResultStatus;
            action.ResultMessage = ResultStatus == RecoveryActionStatus.Succeeded
                ? $"コンテナ {action.TargetResource} のrestartに成功しました。"
                : $"コンテナ {action.TargetResource} のrestartに失敗しました。";
        }

        return Task.CompletedTask;
    }
}
