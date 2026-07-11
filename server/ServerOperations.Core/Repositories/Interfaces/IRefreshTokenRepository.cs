using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    Task AddAsync(RefreshToken token, CancellationToken ct = default);

    /// <summary>同一系列(FamilyId)の未失効トークンをすべて失効させる。</summary>
    Task RevokeFamilyAsync(Guid familyId, DateTime revokedAtUtc, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
