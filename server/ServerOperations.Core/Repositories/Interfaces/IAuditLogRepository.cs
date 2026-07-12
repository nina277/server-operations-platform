using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog entry, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
