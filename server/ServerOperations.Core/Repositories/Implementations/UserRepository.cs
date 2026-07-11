using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Repositories.Implementations;

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
