using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IRecoveryActionRepository
{
    Task<RecoveryAction?> FindByIdAsync(long id, CancellationToken ct = default);

    Task<RecoveryAction?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default);

    Task<List<RecoveryAction>> GetByIncidentAsync(long incidentId, CancellationToken ct = default);

    /// <summary>
    /// 指定対象・アクション・対象リソースについて、指定時刻以降に実行(または実行試行)された履歴。
    /// クールダウン・回数上限・サーキットブレーカーの判定に使う。
    /// </summary>
    Task<List<RecoveryAction>> GetRecentForRateLimitAsync(
        long targetId, string actionId, string? targetResource, DateTime sinceUtc, CancellationToken ct = default);

    Task AddAsync(RecoveryAction action, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IRecoveryApprovalRepository
{
    Task<RecoveryApproval?> FindByIdAsync(long id, CancellationToken ct = default);

    Task<List<RecoveryApproval>> GetByIncidentAsync(long incidentId, CancellationToken ct = default);

    Task AddAsync(RecoveryApproval approval, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IHealthCheckRepository
{
    Task AddAsync(HealthCheck check, CancellationToken ct = default);

    Task<List<HealthCheck>> GetRecentAsync(long targetId, int limit, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
