using ServerOperations.Api.DTOs.Auth;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Api.Tests;

/// <summary>
/// 利用者の管理。役割の割り当ては権限そのものを動かす操作にあたる。
///
/// 要点は「管理者が居なくなる状態を作らせない」ことと、
/// MFAのリセットが乗っ取りの経路にならないようにすること。
/// </summary>
public class UserManagementServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _users = new();
    private readonly FakeMfaCredentialRepository _mfaCredentials = new();
    private readonly FakeRefreshTokenRepository _refreshTokens;
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    public UserManagementServiceTests() => _refreshTokens = new FakeRefreshTokenRepository(_users);

    private UserManagementService CreateSut() =>
        new(_users, _mfaCredentials, _refreshTokens, _audit, _currentUser, _time);

    /// <summary>FakeCurrentUserAccessor の UserId は1。それに合わせて操作者を1にする。</summary>
    private User AddUser(
        long id,
        string username,
        UserRole role = UserRole.Viewer,
        bool isActive = true,
        bool mfaEnabled = false)
    {
        var user = new User
        {
            Id = id,
            Username = username,
            PasswordHash = "hash",
            Role = role,
            IsActive = isActive,
            CreatedAt = BaseTime.UtcDateTime,
            UpdatedAt = BaseTime.UtcDateTime,
        };

        if (mfaEnabled)
        {
            var credential = new MfaCredential
            {
                Id = id,
                UserId = id,
                SecretProtected = "protected",
                IsEnabled = true,
                CreatedAt = BaseTime.UtcDateTime,
            };
            user.MfaCredential = credential;
            _mfaCredentials.Credentials.Add(credential);
        }

        _users.Users.Add(user);
        return user;
    }

    private void AddSession(long userId) => _refreshTokens.Tokens.Add(new RefreshToken
    {
        Id = (int)userId,
        UserId = userId,
        TokenHash = $"hash-{userId}",
        FamilyId = Guid.NewGuid(),
        ExpiresAt = BaseTime.UtcDateTime.AddDays(30),
        CreatedAt = BaseTime.UtcDateTime,
    });

    // --- 追加 ---

    [Fact]
    public async Task 利用者を追加できる()
    {
        var created = await CreateSut().CreateAsync(new CreateUserRequest
        {
            Username = "operator",
            Password = "initial-password-1",
            Role = "Viewer",
        });

        Assert.Equal("operator", created.Username);
        Assert.Equal("Viewer", created.Role);
        Assert.True(created.IsActive);
    }

    [Fact]
    public async Task 初期パスワードを監査に残さない()
    {
        await CreateSut().CreateAsync(new CreateUserRequest
        {
            Username = "operator",
            Password = "initial-password-1",
            Role = "Viewer",
        });

        Assert.All(_audit.Entries,
            e => Assert.DoesNotContain("initial-password-1", e.Details ?? string.Empty));
    }

    [Fact]
    public async Task 追加を監査に残す()
    {
        await CreateSut().CreateAsync(new CreateUserRequest
        {
            Username = "operator",
            Password = "initial-password-1",
            Role = "OperatorAdmin",
        });

        Assert.Contains(_audit.Entries, e => e.Action == "user.create");
    }

    [Fact]
    public async Task 同じユーザー名は追加できない()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().CreateAsync(new CreateUserRequest
            {
                Username = "admin",
                Password = "initial-password-1",
                Role = "Viewer",
            }));

        Assert.Equal("duplicate_username", ex.Code);
    }

    [Fact]
    public async Task パスワードはハッシュにして保存する()
    {
        await CreateSut().CreateAsync(new CreateUserRequest
        {
            Username = "operator",
            Password = "initial-password-1",
            Role = "Viewer",
        });

        var stored = _users.Users.Single(u => u.Username == "operator");
        Assert.NotEqual("initial-password-1", stored.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("initial-password-1", stored.PasswordHash));
    }

    // --- 役割の変更 ---

    [Fact]
    public async Task 役割を変更できる()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer");

        var updated = await CreateSut().UpdateRoleAsync(2, new UpdateUserRoleRequest
        {
            Role = "OperatorAdmin",
        });

        Assert.Equal("OperatorAdmin", updated.Role);
    }

    [Fact]
    public async Task 役割の変更を前後の値つきで監査に残す()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer");

        await CreateSut().UpdateRoleAsync(2, new UpdateUserRoleRequest { Role = "OperatorAdmin" });

        var entry = Assert.Single(_audit.Entries, e => e.Action == "user.role.update");
        Assert.Contains("before=Viewer", entry.Details ?? string.Empty);
        Assert.Contains("after=OperatorAdmin", entry.Details ?? string.Empty);
    }

    [Fact]
    public async Task 自分自身の役割は変えられない()
    {
        // 降格すると、その場で管理操作ができなくなり元へ戻す手段も失う
        AddUser(1, "admin", UserRole.OperatorAdmin);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateRoleAsync(1, new UpdateUserRoleRequest { Role = "Viewer" }));

        Assert.Equal("cannot_change_own_role", ex.Code);
    }

    [Fact]
    public async Task 最後の管理者を降格できない()
    {
        // 管理者が居なくなると、以後は誰も設定を変えられない
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "other-admin", UserRole.OperatorAdmin);
        // 操作者(1)以外の管理者は2だけ。2を降ろすと1人になるが、それは許す。
        AddUser(3, "third-admin", UserRole.OperatorAdmin, isActive: false);

        // 有効な管理者を1人だけにしてから、その1人を降ろそうとする
        _users.Users.Single(u => u.Id == 1).Role = UserRole.Viewer;

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateRoleAsync(2, new UpdateUserRoleRequest { Role = "Viewer" }));

        Assert.Equal("last_admin", ex.Code);
    }

    [Fact]
    public async Task 管理者が他にも居れば降格できる()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "other-admin", UserRole.OperatorAdmin);

        var updated = await CreateSut().UpdateRoleAsync(2, new UpdateUserRoleRequest
        {
            Role = "Viewer",
        });

        Assert.Equal("Viewer", updated.Role);
    }

    [Fact]
    public async Task 存在しない役割は指定できない()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer");

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateRoleAsync(2, new UpdateUserRoleRequest { Role = "SuperAdmin" }));

        Assert.Equal("invalid_role", ex.Code);
    }

    // --- 有効・無効 ---

    [Fact]
    public async Task 利用者を無効にできる()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer");

        var updated = await CreateSut().UpdateActiveAsync(2, new UpdateUserActiveRequest
        {
            IsActive = false,
        });

        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task 無効にしたらセッションを切る()
    {
        // 無効にしたのにログインしたままでは意味がない
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer");
        AddSession(2);

        await CreateSut().UpdateActiveAsync(2, new UpdateUserActiveRequest { IsActive = false });

        Assert.All(_refreshTokens.Tokens.Where(t => t.UserId == 2),
            t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task 自分自身は無効にできない()
    {
        // その場で閉め出される
        AddUser(1, "admin", UserRole.OperatorAdmin);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateActiveAsync(1, new UpdateUserActiveRequest { IsActive = false }));

        Assert.Equal("cannot_deactivate_self", ex.Code);
    }

    [Fact]
    public async Task 最後の管理者を無効にできない()
    {
        AddUser(1, "admin", UserRole.Viewer);
        AddUser(2, "only-admin", UserRole.OperatorAdmin);

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().UpdateActiveAsync(2, new UpdateUserActiveRequest { IsActive = false }));

        Assert.Equal("last_admin", ex.Code);
    }

    [Fact]
    public async Task 有効に戻すのは管理者が1人でもできる()
    {
        // 増やす方向は締め出しにつながらない
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "disabled-admin", UserRole.OperatorAdmin, isActive: false);

        var updated = await CreateSut().UpdateActiveAsync(2, new UpdateUserActiveRequest
        {
            IsActive = true,
        });

        Assert.True(updated.IsActive);
    }

    [Fact]
    public async Task 有効無効の変更を監査に残す()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer");

        await CreateSut().UpdateActiveAsync(2, new UpdateUserActiveRequest { IsActive = false });

        Assert.Contains(_audit.Entries, e => e.Action == "user.active.update");
    }

    // --- MFAのリセット ---

    [Fact]
    public async Task 他人のMFAをリセットできる()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer", mfaEnabled: true);

        var updated = await CreateSut().ResetMfaAsync(2);

        Assert.False(updated.MfaEnabled);
        Assert.DoesNotContain(_mfaCredentials.Credentials, c => c.UserId == 2);
    }

    [Fact]
    public async Task MFAをリセットしたらセッションをすべて切る()
    {
        // リセットはそれ自体が乗っ取りの経路になりうる。
        // 再ログインからやり直させる。
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer", mfaEnabled: true);
        AddSession(2);

        await CreateSut().ResetMfaAsync(2);

        Assert.All(_refreshTokens.Tokens.Where(t => t.UserId == 2),
            t => Assert.NotNull(t.RevokedAt));
    }

    [Fact]
    public async Task MFAのリセットを監査に残す()
    {
        // 誰がリセットしたかを必ず追えるようにする
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer", mfaEnabled: true);

        await CreateSut().ResetMfaAsync(2);

        var entry = Assert.Single(_audit.Entries, e => e.Action == "user.mfa.reset");
        Assert.Equal(1, entry.ActorUserId);
        Assert.Contains("username=viewer", entry.Details ?? string.Empty);
    }

    [Fact]
    public async Task 自分のMFAはこの口からリセットできない()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin, mfaEnabled: true);

        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().ResetMfaAsync(1));

        Assert.Equal("cannot_reset_own_mfa", ex.Code);
    }

    [Fact]
    public async Task MFA未設定の利用者はリセットできない()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);
        AddUser(2, "viewer");

        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().ResetMfaAsync(2));

        Assert.Equal("mfa_not_configured", ex.Code);
    }

    [Fact]
    public async Task 存在しない利用者は操作できない()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin);

        var ex = await Assert.ThrowsAsync<AppException>(() => CreateSut().ResetMfaAsync(999));

        Assert.Equal("user_not_found", ex.Code);
    }

    // --- 一覧 ---

    [Fact]
    public async Task 一覧にパスワードもMFAシークレットも含めない()
    {
        AddUser(1, "admin", UserRole.OperatorAdmin, mfaEnabled: true);

        var all = await CreateSut().GetAllAsync();

        var user = Assert.Single(all);
        Assert.True(user.MfaEnabled);
        // DTOに値そのものを持つ項目が無いことを、型の形として固定する
        Assert.DoesNotContain(typeof(ManagedUserDto).GetProperties(),
            p => p.Name.Contains("Password") || p.Name.Contains("Secret"));
    }
}
