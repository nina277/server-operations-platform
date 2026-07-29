using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class RecoveryActionRepository(AppDbContext db) : IRecoveryActionRepository
{
    public Task<RecoveryAction?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.RecoveryActions.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<RecoveryAction?> FindByIdempotencyKeyAsync(string key, CancellationToken ct = default) =>
        db.RecoveryActions.FirstOrDefaultAsync(a => a.IdempotencyKey == key, ct);

    public Task<List<RecoveryAction>> GetByIncidentAsync(long incidentId, CancellationToken ct = default) =>
        db.RecoveryActions
            .Where(a => a.IncidentId == incidentId)
            .OrderByDescending(a => a.RequestedAt)
            .ToListAsync(ct);

    public Task<List<RecoveryAction>> GetRecentForRateLimitAsync(
        long targetId, string actionId, string? targetResource, DateTime sinceUtc, CancellationToken ct = default) =>
        db.RecoveryActions
            .Where(a => a.TargetId == targetId
                && a.ActionId == actionId
                && a.TargetResource == targetResource
                && a.RequestedAt >= sinceUtc)
            .OrderByDescending(a => a.RequestedAt)
            .ToListAsync(ct);

    public async Task AddAsync(RecoveryAction action, CancellationToken ct = default) =>
        await db.RecoveryActions.AddAsync(action, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class RecoveryApprovalRepository(AppDbContext db) : IRecoveryApprovalRepository
{
    public Task<RecoveryApproval?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.RecoveryApprovals.FirstOrDefaultAsync(a => a.Id == id, ct);

    public Task<List<RecoveryApproval>> GetByIncidentAsync(long incidentId, CancellationToken ct = default) =>
        db.RecoveryApprovals
            .Where(a => a.IncidentId == incidentId)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(RecoveryApproval approval, CancellationToken ct = default) =>
        await db.RecoveryApprovals.AddAsync(approval, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class HealthCheckRepository(AppDbContext db) : IHealthCheckRepository
{
    public async Task AddAsync(HealthCheck check, CancellationToken ct = default) =>
        await db.HealthChecks.AddAsync(check, ct);

    public Task<List<HealthCheck>> GetRecentAsync(long targetId, int limit, CancellationToken ct = default) =>
        db.HealthChecks
            .Where(h => h.TargetId == targetId)
            .OrderByDescending(h => h.CheckedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
