using ServerOperations.Api.Models.Auth;

namespace ServerOperations.Api.Repositories.Interfaces;

public interface IMfaCredentialRepository
{
    Task<MfaCredential?> FindByUserIdAsync(long userId, CancellationToken ct = default);

    Task AddAsync(MfaCredential credential, CancellationToken ct = default);

    Task RemoveAsync(MfaCredential credential, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
