using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Tests.Fakes;

public class FakeMetricSnapshotRepository : IMetricSnapshotRepository
{
    public List<MetricSnapshot> Snapshots { get; } = [];

    public Task AddAsync(MetricSnapshot snapshot, CancellationToken ct = default)
    {
        snapshot.Id = Snapshots.Count + 1;
        Snapshots.Add(snapshot);
        return Task.CompletedTask;
    }

    public Task<List<MetricSnapshot>> GetRecentAsync(long targetId, int limit, CancellationToken ct = default) =>
        Task.FromResult(Snapshots
            .Where(m => m.TargetId == targetId)
            .OrderByDescending(m => m.CollectedAt)
            .Take(limit)
            .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeIncidentRepository : IIncidentRepository
{
    public List<Incident> Incidents { get; } = [];

    public Task<Incident?> FindByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Incidents.FirstOrDefault(i => i.Id == id));

    public Task<Incident?> FindOpenBySignatureAsync(
        long targetId, string signature, CancellationToken ct = default) =>
        Task.FromResult(Incidents
            .Where(i => i.TargetId == targetId
                && i.SignatureSha256 == signature
                && i.Status != IncidentStatus.Closed)
            .OrderByDescending(i => i.LastOccurredAt)
            .FirstOrDefault());

    public Task<(List<Incident> Items, long TotalCount)> SearchAsync(
        IncidentSearchCriteria criteria, CancellationToken ct = default)
    {
        var query = Incidents.AsEnumerable();
        if (!string.IsNullOrEmpty(criteria.Status) &&
            Enum.TryParse<IncidentStatus>(criteria.Status, true, out var status))
        {
            query = query.Where(i => i.Status == status);
        }

        if (criteria.TargetId is { } targetId)
        {
            query = query.Where(i => i.TargetId == targetId);
        }

        var list = query.OrderByDescending(i => i.LastOccurredAt).ToList();
        return Task.FromResult((
            list.Skip((criteria.Page - 1) * criteria.PageSize).Take(criteria.PageSize).ToList(),
            (long)list.Count));
    }

    public Task<Dictionary<IncidentStatus, int>> CountByStatusAsync(CancellationToken ct = default) =>
        Task.FromResult(Incidents.GroupBy(i => i.Status).ToDictionary(g => g.Key, g => g.Count()));

    public Task<Dictionary<IncidentSeverity, int>> CountActiveBySeverityAsync(CancellationToken ct = default) =>
        Task.FromResult(Incidents
            .Where(i => i.Status != IncidentStatus.Resolved && i.Status != IncidentStatus.Closed)
            .GroupBy(i => i.Severity)
            .ToDictionary(g => g.Key, g => g.Count()));

    public Task AddAsync(Incident incident, CancellationToken ct = default)
    {
        incident.Id = Incidents.Count + 1;
        Incidents.Add(incident);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeIncidentLogRepository : IIncidentLogRepository
{
    public List<IncidentLog> Logs { get; } = [];

    public Task AddAsync(IncidentLog log, CancellationToken ct = default)
    {
        log.Id = Logs.Count + 1;
        Logs.Add(log);
        return Task.CompletedTask;
    }

    public Task<List<IncidentLog>> GetRecentAsync(long targetId, int limit, CancellationToken ct = default) =>
        Task.FromResult(Logs
            .Where(l => l.TargetId == targetId)
            .OrderByDescending(l => l.CollectedAt)
            .Take(limit)
            .ToList());

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}
