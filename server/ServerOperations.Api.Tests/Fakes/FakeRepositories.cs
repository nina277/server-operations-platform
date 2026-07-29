using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Tests.Fakes;

public class FakeUserRepository : IUserRepository
{
    public List<User> Users { get; } = [];

    public Task<User?> FindByUsernameAsync(string username, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Username == username));

    public Task<User?> FindByIdAsync(long id, CancellationToken ct = default) =>
        Task.FromResult(Users.FirstOrDefault(u => u.Id == id));

    public Task<bool> AnyAsync(CancellationToken ct = default) => Task.FromResult(Users.Count > 0);

    public Task AddAsync(User user, CancellationToken ct = default)
    {
        user.Id = Users.Count + 1;
        Users.Add(user);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeRefreshTokenRepository(FakeUserRepository? users = null) : IRefreshTokenRepository
{
    public List<RefreshToken> Tokens { get; } = [];

    public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var token = Tokens.FirstOrDefault(t => t.TokenHash == tokenHash);

        // EF実装のInclude(t => t.User)相当: Userナビゲーションを解決する
        if (token is { User: null } && users is not null)
        {
            token.User = users.Users.FirstOrDefault(u => u.Id == token.UserId);
        }

        return Task.FromResult(token);
    }

    public Task AddAsync(RefreshToken token, CancellationToken ct = default)
    {
        token.Id = Tokens.Count + 1;
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task RevokeFamilyAsync(Guid familyId, DateTime revokedAtUtc, CancellationToken ct = default)
    {
        foreach (var token in Tokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null))
        {
            token.RevokedAt = revokedAtUtc;
        }

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public class FakeMfaCredentialRepository : IMfaCredentialRepository
{
    public List<MfaCredential> Credentials { get; } = [];

    public Task<MfaCredential?> FindByUserIdAsync(long userId, CancellationToken ct = default) =>
        Task.FromResult(Credentials.FirstOrDefault(c => c.UserId == userId));

    public Task AddAsync(MfaCredential credential, CancellationToken ct = default)
    {
        credential.Id = Credentials.Count + 1;
        Credentials.Add(credential);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(MfaCredential credential, CancellationToken ct = default)
    {
        Credentials.Remove(credential);
        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
}

public record AuditEntry(string Action, string TargetType, string? TargetId, AuditResult Result, long? ActorUserId);

public class FakeAuditService : IAuditService
{
    public List<AuditEntry> Entries { get; } = [];

    public Task RecordAsync(
        string action,
        string targetType,
        string? targetId,
        AuditResult result,
        long? actorUserId = null,
        string? actorName = null,
        string? details = null,
        CancellationToken ct = default)
    {
        Entries.Add(new AuditEntry(action, targetType, targetId, result, actorUserId));
        return Task.CompletedTask;
    }
}

public class FakeMfaService : IMfaService
{
    public bool ValidateResult { get; set; } = true;

    public Task<ServerOperations.Api.DTOs.Auth.MfaSetupResponse> SetupAsync(
        long userId, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<ServerOperations.Api.DTOs.Auth.MfaVerifyResponse> VerifyAsync(
        long userId, string totpCode, CancellationToken ct = default) => throw new NotImplementedException();

    public Task<bool> ValidateForLoginAsync(long userId, string totpCode, CancellationToken ct = default) =>
        Task.FromResult(ValidateResult);
}

public class TestTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
