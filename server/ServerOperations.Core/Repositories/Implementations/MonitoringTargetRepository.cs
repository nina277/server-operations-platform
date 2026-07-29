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

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
