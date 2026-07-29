using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class AuditLogRepository(AppDbContext db) : IAuditLogRepository
{
    public async Task AddAsync(AuditLog entry, CancellationToken ct = default) =>
        await db.AuditLogs.AddAsync(entry, ct);

    public async Task<(List<AuditLog> Items, long TotalCount)> SearchAsync(
        AuditLogFilter filter, int skip, int take, CancellationToken ct = default)
    {
        var query = Filter(filter);

        var total = await query.LongCountAsync(ct);
        var items = await query
            .OrderByDescending(a => a.OccurredAt)
            .ThenByDescending(a => a.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<(List<string> TargetTypes, List<string> Actions)> GetFilterOptionsAsync(
        CancellationToken ct = default)
    {
        var targetTypes = await db.AuditLogs
            .Select(a => a.TargetType)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync(ct);

        var actions = await db.AuditLogs
            .Select(a => a.Action)
            .Distinct()
            .OrderBy(a => a)
            .ToListAsync(ct);

        return (targetTypes, actions);
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);

    private IQueryable<AuditLog> Filter(AuditLogFilter filter)
    {
        var query = db.AuditLogs.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.ActorName))
        {
            // 操作者名は部分一致で探せるようにする(調査時に完全一致が分からないことが多いため)
            var actorName = filter.ActorName.Trim();
            query = query.Where(a => a.ActorName != null && a.ActorName.Contains(actorName));
        }

        if (!string.IsNullOrWhiteSpace(filter.TargetType))
        {
            query = query.Where(a => a.TargetType == filter.TargetType);
        }

        if (!string.IsNullOrWhiteSpace(filter.Action))
        {
            query = query.Where(a => a.Action == filter.Action);
        }

        if (filter.Result is { } result)
        {
            query = query.Where(a => a.Result == result);
        }

        if (filter.OccurredFromUtc is { } from)
        {
            query = query.Where(a => a.OccurredAt >= from);
        }

        if (filter.OccurredToUtc is { } to)
        {
            query = query.Where(a => a.OccurredAt <= to);
        }

        return query;
    }
}
