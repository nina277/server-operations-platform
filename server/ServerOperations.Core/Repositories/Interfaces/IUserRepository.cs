using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Core.Repositories.Interfaces;

public interface IUserRepository
{
    Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default);

    Task<User?> FindByIdAsync(long id, CancellationToken ct = default);

    Task<bool> AnyAsync(CancellationToken ct = default);

    /// <summary>利用者の一覧。件数が多くならない前提で全件返す。</summary>
    Task<List<User>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 有効な管理者の人数。最後の1人を無効化・降格させないために使う。
    /// </summary>
    Task<int> CountActiveAdminsAsync(CancellationToken ct = default);

    Task AddAsync(User user, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
