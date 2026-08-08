using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class MonitoringTargetRepository(AppDbContext db) : IMonitoringTargetRepository
{
    public Task<MonitoringTarget?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.MonitoringTargets
            .Include(t => t.Profile)
            .Include(t => t.Credentials)
            .FirstOrDefaultAsync(t => t.Id == id, ct);

    public Task<MonitoringTarget?> FindByNameAsync(string name, CancellationToken ct = default) =>
        db.MonitoringTargets.FirstOrDefaultAsync(t => t.Name == name, ct);

    public Task<List<MonitoringTarget>> GetAllAsync(CancellationToken ct = default) =>
        db.MonitoringTargets
            .Include(t => t.Profile)
            .Include(t => t.Credentials)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);

    public async Task AddAsync(MonitoringTarget target, CancellationToken ct = default) =>
        await db.MonitoringTargets.AddAsync(target, ct);

    public async Task<TargetDependents> CountDependentsAsync(
        long targetId, CancellationToken ct = default)
    {
        var incidentIds = await db.Incidents
            .Where(i => i.TargetId == targetId)
            .Select(i => i.Id)
            .ToListAsync(ct);

        return new TargetDependents
        {
            MetricSnapshots = await db.MetricSnapshots.CountAsync(m => m.TargetId == targetId, ct),
            Incidents = incidentIds.Count,
            IncidentLogs = await db.IncidentLogs.CountAsync(l => l.TargetId == targetId, ct),
            Diagnoses = await db.Diagnoses.CountAsync(d => d.TargetId == targetId, ct),
            RecoveryActions = await db.RecoveryActions.CountAsync(a => a.TargetId == targetId, ct),
            HealthChecks = await db.HealthChecks.CountAsync(h => h.TargetId == targetId, ct),
            Notifications = await db.Notifications.CountAsync(n => n.TargetId == targetId, ct),
            MaintenanceWindows =
                await db.MaintenanceWindows.CountAsync(w => w.TargetId == targetId, ct),
        };
    }

    public async Task DeleteWithDependentsAsync(
        MonitoringTarget target, CancellationToken ct = default)
    {
        var targetId = target.Id;

        var incidentIds = await db.Incidents
            .Where(i => i.TargetId == targetId)
            .Select(i => i.Id)
            .ToListAsync(ct);

        // 途中で失敗したときに、対象だけ消えて履歴が残る状態にしない
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // インシデントに紐づくものから先に消す
        await db.IncidentNotes.Where(n => incidentIds.Contains(n.IncidentId))
            .ExecuteDeleteAsync(ct);
        await db.RecoveryApprovals.Where(a => incidentIds.Contains(a.IncidentId))
            .ExecuteDeleteAsync(ct);

        await db.HealthChecks.Where(h => h.TargetId == targetId).ExecuteDeleteAsync(ct);
        await db.RecoveryActions.Where(a => a.TargetId == targetId).ExecuteDeleteAsync(ct);
        await db.Diagnoses.Where(d => d.TargetId == targetId).ExecuteDeleteAsync(ct);
        await db.IncidentLogs.Where(l => l.TargetId == targetId).ExecuteDeleteAsync(ct);
        await db.MetricSnapshots.Where(m => m.TargetId == targetId).ExecuteDeleteAsync(ct);
        await db.Notifications.Where(n => n.TargetId == targetId).ExecuteDeleteAsync(ct);
        await db.MaintenanceWindows.Where(w => w.TargetId == targetId).ExecuteDeleteAsync(ct);
        await db.Incidents.Where(i => i.TargetId == targetId).ExecuteDeleteAsync(ct);

        // Profile と Credentials は外部キーのカスケードで消える
        db.MonitoringTargets.Remove(target);
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
