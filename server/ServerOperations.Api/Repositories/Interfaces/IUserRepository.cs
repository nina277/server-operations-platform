using ServerOperations.Api.Models.Auth;

namespace ServerOperations.Api.Repositories.Interfaces;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);

    Task<User?> FindByIdAsync(long id, CancellationToken ct = default);

    Task<bool> AnyAsync(CancellationToken ct = default);

    Task AddAsync(User user, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
