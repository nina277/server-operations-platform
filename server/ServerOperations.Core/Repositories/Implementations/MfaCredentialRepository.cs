using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class MfaCredentialRepository(AppDbContext db) : IMfaCredentialRepository
{
    public Task<MfaCredential?> FindByUserIdAsync(long userId, CancellationToken ct = default) =>
        db.MfaCredentials.FirstOrDefaultAsync(m => m.UserId == userId, ct);

    public async Task AddAsync(MfaCredential credential, CancellationToken ct = default) =>
        await db.MfaCredentials.AddAsync(credential, ct);

    public Task RemoveAsync(MfaCredential credential, CancellationToken ct = default)
    {
        db.MfaCredentials.Remove(credential);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
