using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class AiUsageRecordRepository(AppDbContext db) : IAiUsageRecordRepository
{
    public async Task AddAsync(AiUsageRecord record, CancellationToken ct = default) =>
        await db.AiUsageRecords.AddAsync(record, ct);

    public Task<int> CountSinceAsync(DateTime sinceUtc, CancellationToken ct = default) =>
        db.AiUsageRecords.CountAsync(r => r.CalledAt >= sinceUtc, ct);

    public Task<List<AiUsageRecord>> GetRecentAsync(int limit, CancellationToken ct = default) =>
        db.AiUsageRecords
            .OrderByDescending(r => r.CalledAt)
            .Take(Math.Clamp(limit, 1, 200))
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class AiUsageLimitRepository(AppDbContext db) : IAiUsageLimitRepository
{
    public Task<AiUsageLimit?> GetAsync(CancellationToken ct = default) =>
        db.AiUsageLimits.OrderBy(l => l.Id).FirstOrDefaultAsync(ct);

    public async Task AddAsync(AiUsageLimit limit, CancellationToken ct = default) =>
        await db.AiUsageLimits.AddAsync(limit, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
