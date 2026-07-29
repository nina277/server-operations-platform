using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IMonitoringTargetRepository
{
    /// <summary>Profile・Credentialsを含めて取得する。</summary>
    Task<MonitoringTarget?> FindByIdAsync(long id, CancellationToken ct = default);

    Task<MonitoringTarget?> FindByNameAsync(string name, CancellationToken ct = default);

    Task<List<MonitoringTarget>> GetAllAsync(CancellationToken ct = default);

    Task AddAsync(MonitoringTarget target, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
