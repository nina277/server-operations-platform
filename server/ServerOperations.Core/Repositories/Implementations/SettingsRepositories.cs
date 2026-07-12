using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class SystemSettingRepository(AppDbContext db) : ISystemSettingRepository
{
    public Task<SystemSetting?> FindByCategoryAsync(SettingCategory category, CancellationToken ct = default) =>
        db.SystemSettings.FirstOrDefaultAsync(s => s.Category == category, ct);

    public async Task AddAsync(SystemSetting setting, CancellationToken ct = default) =>
        await db.SystemSettings.AddAsync(setting, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class EncryptedSecretRepository(AppDbContext db) : IEncryptedSecretRepository
{
    public Task<EncryptedSecret?> FindByKindAsync(string kind, CancellationToken ct = default) =>
        db.EncryptedSecrets.FirstOrDefaultAsync(s => s.Kind == kind, ct);

    public async Task AddAsync(EncryptedSecret secret, CancellationToken ct = default) =>
        await db.EncryptedSecrets.AddAsync(secret, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}

public class TrustedNetworkCidrRepository(AppDbContext db) : ITrustedNetworkCidrRepository
{
    public Task<List<TrustedNetworkCidr>> GetAllAsync(CancellationToken ct = default) =>
        db.TrustedNetworkCidrs.OrderBy(c => c.Id).ToListAsync(ct);

    public Task<TrustedNetworkCidr?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.TrustedNetworkCidrs.FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task AddAsync(TrustedNetworkCidr cidr, CancellationToken ct = default) =>
        await db.TrustedNetworkCidrs.AddAsync(cidr, ct);

    public Task RemoveAsync(TrustedNetworkCidr cidr, CancellationToken ct = default)
    {
        db.TrustedNetworkCidrs.Remove(cidr);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
