using ServerOperations.Api.Data;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Repositories.Interfaces;

namespace ServerOperations.Api.Repositories.Implementations;

public class AuditLogRepository(AppDbContext db) : IAuditLogRepository
{
    public async Task AddAsync(AuditLog entry, CancellationToken ct = default) =>
        await db.AuditLogs.AddAsync(entry, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
