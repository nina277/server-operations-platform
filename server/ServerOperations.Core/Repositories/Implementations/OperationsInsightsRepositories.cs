using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class IncidentNoteRepository(AppDbContext db) : IIncidentNoteRepository
{
    public Task<List<IncidentNote>> GetForIncidentAsync(long incidentId, CancellationToken ct = default) =>
        db.IncidentNotes
            .Where(n => n.IncidentId == incidentId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(ct);

    public async Task AddAsync(IncidentNote note, CancellationToken ct = default) =>
        await db.IncidentNotes.AddAsync(note, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class MaintenanceWindowRepository(AppDbContext db) : IMaintenanceWindowRepository
{
    public Task<MaintenanceWindow?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.MaintenanceWindows.FirstOrDefaultAsync(w => w.Id == id, ct);

    public Task<List<MaintenanceWindow>> GetUpcomingAsync(DateTime asOf, CancellationToken ct = default) =>
        db.MaintenanceWindows
            .Where(w => w.CancelledAt == null && w.EndsAt >= asOf)
            .OrderBy(w => w.StartsAt)
            .ToListAsync(ct);

    public Task<List<MaintenanceWindow>> GetActiveForTargetAsync(
        long targetId, DateTime asOf, CancellationToken ct = default) =>
        db.MaintenanceWindows
            .Where(w => w.CancelledAt == null
                && w.StartsAt <= asOf
                && w.EndsAt > asOf
                // TargetIdがnullの期間はすべての対象に効く
                && (w.TargetId == null || w.TargetId == targetId))
            .ToListAsync(ct);

    public async Task AddAsync(MaintenanceWindow window, CancellationToken ct = default) =>
        await db.MaintenanceWindows.AddAsync(window, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class OperationsInsightsRepository(AppDbContext db) : IOperationsInsightsRepository
{
    public async Task<List<double>> GetDetectionToNotificationSecondsAsync(
        OperationsInsightsRange range, CancellationToken ct = default)
    {
        // 検知(インシデントの初回発生)から最初の通知までを測る。
        // 通知が作られていないインシデントは対象外(抑止中や重大度未満)。
        var pairs = await db.Notifications
            .Where(n => n.IncidentId != null
                && n.FirstNotifiedAt >= range.From
                && n.FirstNotifiedAt < range.To)
            .Join(db.Incidents,
                n => n.IncidentId,
                i => i.Id,
                (n, i) => new { i.FirstOccurredAt, n.FirstNotifiedAt })
            .ToListAsync(ct);

        return pairs
            .Select(p => (p.FirstNotifiedAt - p.FirstOccurredAt).TotalSeconds)
            // 時刻のずれで負になったものは指標として意味がないため落とす
            .Where(seconds => seconds >= 0)
            .ToList();
    }

    public async Task<List<double>> GetRecoveryDurationSecondsAsync(
        OperationsInsightsRange range, bool automaticOnly, CancellationToken ct = default)
    {
        var query = FilterRecovery(range, automaticOnly)
            .Where(a => a.CompletedAt != null && a.Status == RecoveryActionStatus.Succeeded);

        var pairs = await query
            .Select(a => new { a.RequestedAt, a.CompletedAt })
            .ToListAsync(ct);

        return pairs
            .Select(p => (p.CompletedAt!.Value - p.RequestedAt).TotalSeconds)
            .Where(seconds => seconds >= 0)
            .ToList();
    }

    public async Task<Dictionary<RecoveryActionStatus, int>> CountRecoveryByStatusAsync(
        OperationsInsightsRange range, bool automaticOnly, CancellationToken ct = default)
    {
        var rows = await FilterRecovery(range, automaticOnly)
            .GroupBy(a => a.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Status, r => r.Count);
    }

    public async Task<Dictionary<string, int>> CountBlockedReasonsAsync(
        OperationsInsightsRange range, CancellationToken ct = default)
    {
        var rows = await db.RecoveryActions
            .Where(a => a.RequestedAt >= range.From
                && a.RequestedAt < range.To
                && a.BlockedReason != null)
            .GroupBy(a => a.BlockedReason!)
            .Select(g => new { Reason = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Reason, r => r.Count);
    }

    public async Task<Dictionary<IncidentSeverity, int>> CountIncidentsBySeverityAsync(
        OperationsInsightsRange range, CancellationToken ct = default)
    {
        var rows = await db.Incidents
            .Where(i => i.FirstOccurredAt >= range.From && i.FirstOccurredAt < range.To)
            .GroupBy(i => i.Severity)
            .Select(g => new { Severity = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return rows.ToDictionary(r => r.Severity, r => r.Count);
    }

    public async Task<(int Detected, int Resolved)> CountIncidentOutcomesAsync(
        OperationsInsightsRange range, CancellationToken ct = default)
    {
        var detected = await db.Incidents
            .CountAsync(i => i.FirstOccurredAt >= range.From && i.FirstOccurredAt < range.To, ct);

        var resolved = await db.Incidents
            .CountAsync(i => i.FirstOccurredAt >= range.From
                && i.FirstOccurredAt < range.To
                && i.ResolvedAt != null, ct);

        return (detected, resolved);
    }

    public async Task<RecurrenceSummary> GetRecurrenceAsync(
        long targetId, string signature, long excludeIncidentId, CancellationToken ct = default)
    {
        var past = await db.Incidents
            .Where(i => i.TargetId == targetId
                && i.SignatureSha256 == signature
                && i.Id != excludeIncidentId)
            .Select(i => new { i.Id, i.FirstOccurredAt, i.ResolvedAt })
            .ToListAsync(ct);

        if (past.Count == 0)
        {
            return new RecurrenceSummary { TotalCount = 0, ResolvedCount = 0 };
        }

        var pastIds = past.Select(p => p.Id).ToList();

        // 前回この障害を解決した操作。何をすれば直るかの手掛かりになる。
        var lastSuccess = await db.RecoveryActions
            .Where(a => pastIds.Contains(a.IncidentId)
                && a.Status == RecoveryActionStatus.Succeeded
                && a.CompletedAt != null)
            .OrderByDescending(a => a.CompletedAt)
            .Select(a => new { a.ActionId, a.CompletedAt })
            .FirstOrDefaultAsync(ct);

        return new RecurrenceSummary
        {
            TotalCount = past.Count,
            ResolvedCount = past.Count(p => p.ResolvedAt != null),
            FirstOccurredAt = past.Min(p => p.FirstOccurredAt),
            PreviousOccurredAt = past.Max(p => p.FirstOccurredAt),
            LastSuccessfulActionId = lastSuccess?.ActionId,
            LastSuccessfulAt = lastSuccess?.CompletedAt,
        };
    }

    /// <summary>自動実行は要求者が居ないもの(RequestedByUserIdがnull)で見分ける。</summary>
    private IQueryable<RecoveryAction> FilterRecovery(OperationsInsightsRange range, bool automaticOnly)
    {
        var query = db.RecoveryActions
            .Where(a => a.RequestedAt >= range.From && a.RequestedAt < range.To);

        return automaticOnly ? query.Where(a => a.RequestedByUserId == null) : query;
    }
}
