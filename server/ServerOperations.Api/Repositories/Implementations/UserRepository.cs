using Microsoft.EntityFrameworkCore;
using ServerOperations.Api.Data;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Repositories.Interfaces;

namespace ServerOperations.Api.Repositories.Implementations;

public class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default) =>
        db.Users.Include(u => u.MfaCredential)
            .FirstOrDefaultAsync(u => u.Username == username, ct);

    public Task<User?> FindByIdAsync(long id, CancellationToken ct = default) =>
        db.Users.Include(u => u.MfaCredential)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<bool> AnyAsync(CancellationToken ct = default) => db.Users.AnyAsync(ct);

    public async Task AddAsync(User user, CancellationToken ct = default) =>
        await db.Users.AddAsync(user, ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct);
}
