using System.Net;
using ServerOperations.Api.Models.Settings;
using ServerOperations.Api.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Tests.Fakes;

public class FakeSystemSettingRepository : ISystemSettingRepository
{
    public List<SystemSetting> Settings { get; } = [];

    public Task<SystemSetting?> FindByCategoryAsync(SettingCategory category, CancellationToken ct = default) =>
        Task.FromResult(Settings.FirstOrDefault(s => s.Category == category));

    public Task AddAsync(SystemSetting setting, CancellationToken ct = default)
    {
        setting.Id = Settings.Count + 1;
        Settings.Add(setting);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeEncryptedSecretRepository : IEncryptedSecretRepository
{
    public List<EncryptedSecret> Secrets { get; } = [];

    public Task<EncryptedSecret?> FindByKindAsync(string kind, CancellationToken ct = default) =>
        Task.FromResult(Secrets.FirstOrDefault(s => s.Kind == kind));

    public Task AddAsync(EncryptedSecret secret, CancellationToken ct = default)
    {
        secret.Id = Secrets.Count + 1;
        Secrets.Add(secret);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeTrustedNetworkCidrRepository : ITrustedNetworkCidrRepository
{
    public List<TrustedNetworkCidr> Cidrs { get; } = [];

    public Task<List<TrustedNetworkCidr>> GetAllAsync(CancellationToken ct = default) =>
        Task.FromResult(Cidrs.OrderBy(c => c.Id).ToList());

    public Task<TrustedNetworkCidr?> FindByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Cidrs.FirstOrDefault(c => c.Id == id));

    public Task AddAsync(TrustedNetworkCidr cidr, CancellationToken ct = default)
    {
        cidr.Id = Cidrs.Count + 1;
        Cidrs.Add(cidr);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(TrustedNetworkCidr cidr, CancellationToken ct = default)
    {
        Cidrs.Remove(cidr);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeCurrentUserAccessor : ICurrentUserAccessor
{
    public long? UserId { get; set; } = 1;

    public string? Username { get; set; } = "admin";

    public IPAddress? RemoteIp { get; set; } = IPAddress.Parse("192.168.1.10");
}
