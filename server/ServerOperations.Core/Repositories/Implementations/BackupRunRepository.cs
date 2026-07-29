using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class BackupRunRepository(AppDbContext db) : IBackupRunRepository
{
    public async Task AddAsync(BackupRun run, CancellationToken ct = default) =>
        await db.BackupRuns.AddAsync(run, ct);

    public Task<List<BackupRun>> GetRecentAsync(int limit, CancellationToken ct = default) =>
        db.BackupRuns
            .OrderByDescending(r => r.StartedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
