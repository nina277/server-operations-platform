using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class MetricSnapshotRepository(AppDbContext db) : IMetricSnapshotRepository
{
    public async Task AddAsync(MetricSnapshot snapshot, CancellationToken ct = default) =>
        await db.MetricSnapshots.AddAsync(snapshot, ct);

    public Task<List<MetricSnapshot>> GetRecentAsync(long targetId, int limit, CancellationToken ct = default) =>
        db.MetricSnapshots
            .Where(m => m.TargetId == targetId)
            .OrderByDescending(m => m.CollectedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class IncidentRepository(AppDbContext db) : IIncidentRepository
{
    public Task<Incident?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.Incidents.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<Incident?> FindOpenBySignatureAsync(
        long targetId, string signature, CancellationToken ct = default) =>
        db.Incidents
            .Where(i => i.TargetId == targetId
                && i.SignatureSha256 == signature
                && i.Status != IncidentStatus.Closed)
            .OrderByDescending(i => i.LastOccurredAt)
            .FirstOrDefaultAsync(ct);

    public async Task<(List<Incident> Items, long TotalCount)> SearchAsync(
        IncidentSearchCriteria criteria, CancellationToken ct = default)
    {
        var query = db.Incidents.AsQueryable();

        if (!string.IsNullOrEmpty(criteria.Status) &&
            Enum.TryParse<IncidentStatus>(criteria.Status, ignoreCase: true, out var status))
        {
            query = query.Where(i => i.Status == status);
        }

        if (!string.IsNullOrEmpty(criteria.Severity) &&
            Enum.TryParse<IncidentSeverity>(criteria.Severity, ignoreCase: true, out var severity))
        {
            query = query.Where(i => i.Severity == severity);
        }

        if (criteria.TargetId is { } targetId)
        {
            query = query.Where(i => i.TargetId == targetId);
        }

        if (!string.IsNullOrWhiteSpace(criteria.Search))
        {
            var term = criteria.Search.Trim();
            query = query.Where(i => i.Title.Contains(term) || i.Classification.Contains(term));
        }

        var total = await query.LongCountAsync(ct);

        var descending = criteria.Sort.StartsWith('-');
        var sortKey = criteria.Sort.TrimStart('-');
        query = (sortKey, descending) switch
        {
            ("firstOccurredAt", true) => query.OrderByDescending(i => i.FirstOccurredAt),
            ("firstOccurredAt", false) => query.OrderBy(i => i.FirstOccurredAt),
            ("severity", true) => query.OrderByDescending(i => i.Severity),
            ("severity", false) => query.OrderBy(i => i.Severity),
            ("status", true) => query.OrderByDescending(i => i.Status),
            ("status", false) => query.OrderBy(i => i.Status),
            (_, false) => query.OrderBy(i => i.LastOccurredAt),
            _ => query.OrderByDescending(i => i.LastOccurredAt),
        };

        var page = Math.Max(criteria.Page, 1);
        var pageSize = Math.Clamp(criteria.PageSize, 1, 100);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        return (items, total);
    }

    public async Task<Dictionary<IncidentStatus, int>> CountByStatusAsync(CancellationToken ct = default) =>
        await db.Incidents
            .GroupBy(i => i.Status)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task<Dictionary<IncidentSeverity, int>> CountActiveBySeverityAsync(CancellationToken ct = default) =>
        await db.Incidents
            .Where(i => i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed)
            .GroupBy(i => i.Severity)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);

    public async Task AddAsync(Incident incident, CancellationToken ct = default) =>
        await db.Incidents.AddAsync(incident, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class IncidentLogRepository(AppDbContext db) : IIncidentLogRepository
{
    public async Task AddAsync(IncidentLog log, CancellationToken ct = default) =>
        await db.IncidentLogs.AddAsync(log, ct);

    public Task<List<IncidentLog>> GetRecentAsync(long targetId, int limit, CancellationToken ct = default) =>
        db.IncidentLogs
            .Where(l => l.TargetId == targetId)
            .OrderByDescending(l => l.CollectedAt)
            .Take(limit)
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
