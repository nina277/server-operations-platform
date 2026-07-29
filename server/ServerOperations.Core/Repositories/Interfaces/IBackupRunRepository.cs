using ServerOperations.Core.Models.Settings;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IBackupRunRepository
{
    Task AddAsync(BackupRun run, CancellationToken ct = default);

    Task<List<BackupRun>> GetRecentAsync(int limit, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
