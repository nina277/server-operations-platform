using ServerOperations.Core.Models.Settings;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface ISystemSettingRepository
{
    Task<SystemSetting?> FindByCategoryAsync(SettingCategory category, CancellationToken ct = default);

    Task AddAsync(SystemSetting setting, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface IEncryptedSecretRepository
{
    Task<EncryptedSecret?> FindByKindAsync(string kind, CancellationToken ct = default);

    Task AddAsync(EncryptedSecret secret, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}

public interface ITrustedNetworkCidrRepository
{
    Task<List<TrustedNetworkCidr>> GetAllAsync(CancellationToken ct = default);

    Task<TrustedNetworkCidr?> FindByIdAsync(long id, CancellationToken ct = default);

    Task AddAsync(TrustedNetworkCidr cidr, CancellationToken ct = default);

    Task RemoveAsync(TrustedNetworkCidr cidr, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
