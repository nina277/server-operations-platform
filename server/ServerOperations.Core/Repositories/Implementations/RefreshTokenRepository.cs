using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

public class RefreshTokenRepository(AppDbContext db) : IRefreshTokenRepository
{
    public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default) =>
        db.RefreshTokens.Include(t => t.User)
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, ct);

    public async Task AddAsync(RefreshToken token, CancellationToken ct = default) =>
        await db.RefreshTokens.AddAsync(token, ct);

    public async Task RevokeFamilyAsync(Guid familyId, DateTime revokedAtUtc, CancellationToken ct = default)
    {
        var active = await db.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.RevokedAt = revokedAtUtc;
        }
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
