using ServerOperations.Api.Models.Auth;

namespace ServerOperations.Api.Repositories.Interfaces;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog entry, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
