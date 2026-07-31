using ServerOperations.Api.DTOs.Auth;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>
/// 利用者の管理。役割を3つ定義しているのに割り当てる口が無く、
/// 初期管理者1人でしか運用できなかったため追加する。
///
/// 削除ではなく無効化にとどめる。監査ログは操作者IDを持つため、
/// 利用者を消すと過去の記録から誰の操作か辿れなくなる。
/// </summary>
public class UserManagementService(
    IUserRepository users,
    IMfaCredentialRepository mfaCredentials,
    IRefreshTokenRepository refreshTokens,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IUserManagementService
{
    public async Task<List<ManagedUserDto>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await users.GetAllAsync(ct);
        return all.Select(ToDto).ToList();
    }

    public async Task<ManagedUserDto> CreateAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        var username = request.Username.Trim();

        var existing = await users.FindByUsernameAsync(username, ct);
        if (existing is not null)
        {
            throw AppException.Conflict("duplicate_username", "同じユーザー名が既に存在します。");
        }

        var role = ParseRole(request.Role);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var user = new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = role,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await users.AddAsync(user, ct);
        await users.SaveChangesAsync(ct);

        // 初期パスワードは監査に載せない
        await audit.RecordAsync(
            "user.create", "User", user.Id.ToString(), AuditResult.Success,
            currentUser.UserId, currentUser.Username,
            $"username={username} role={role}", ct);

        return ToDto(user);
    }

    public async Task<ManagedUserDto> UpdateRoleAsync(
        long userId, UpdateUserRoleRequest request, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);
        var role = ParseRole(request.Role);

        // 自分の役割は変えられない。降格すると、その場で管理操作ができなくなり
        // 元へ戻す手段も失う。
        if (userId == currentUser.UserId)
        {
            throw AppException.BadRequest(
                "cannot_change_own_role", "自分自身の役割は変更できません。");
        }

        // 管理者が居なくなると、以後は誰も設定を変えられない
        if (user.Role == UserRole.OperatorAdmin && role != UserRole.OperatorAdmin)
        {
            await RequireAnotherActiveAdminAsync(user, ct);
        }

        var before = user.Role;
        user.Role = role;
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await users.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "user.role.update", "User", user.Id.ToString(), AuditResult.Success,
            currentUser.UserId, currentUser.Username,
            $"username={user.Username} before={before} after={role}", ct);

        return ToDto(user);
    }

    public async Task<ManagedUserDto> UpdateActiveAsync(
        long userId, UpdateUserActiveRequest request, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);

        // 自分自身を無効化すると、その場で閉め出される
        if (userId == currentUser.UserId && !request.IsActive)
        {
            throw AppException.BadRequest(
                "cannot_deactivate_self", "自分自身を無効にすることはできません。");
        }

        if (user.Role == UserRole.OperatorAdmin && !request.IsActive)
        {
            await RequireAnotherActiveAdminAsync(user, ct);
        }

        var before = user.IsActive;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        user.IsActive = request.IsActive;
        user.UpdatedAt = now;
        await users.SaveChangesAsync(ct);

        // 無効にしたのにログインしたままでは意味がない。
        // アクセストークンは有効期限まで生きるが、更新はできなくなる。
        if (!request.IsActive)
        {
            await refreshTokens.RevokeAllForUserAsync(user.Id, now, ct);
            await refreshTokens.SaveChangesAsync(ct);
        }

        await audit.RecordAsync(
            "user.active.update", "User", user.Id.ToString(), AuditResult.Success,
            currentUser.UserId, currentUser.Username,
            $"username={user.Username} before={before} after={request.IsActive}", ct);

        return ToDto(user);
    }

    public async Task<ManagedUserDto> ResetMfaAsync(long userId, CancellationToken ct = default)
    {
        var user = await RequireUserAsync(userId, ct);

        // 自分のMFAはアカウント画面から再設定できる。
        // ここを自分にも開くと、リセットの経路が一つ増えるだけで利点が無い。
        if (userId == currentUser.UserId)
        {
            throw AppException.BadRequest(
                "cannot_reset_own_mfa",
                "自分のMFAはアカウント画面から再設定してください。");
        }

        var credential = await mfaCredentials.FindByUserIdAsync(userId, ct);
        if (credential is null)
        {
            throw AppException.BadRequest("mfa_not_configured", "この利用者はMFAを設定していません。");
        }

        await mfaCredentials.RemoveAsync(credential, ct);
        await mfaCredentials.SaveChangesAsync(ct);

        // MFAのリセットは、それ自体が乗っ取りの経路になりうる。
        // 対象のセッションをすべて切り、再ログインからやり直させる。
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await refreshTokens.RevokeAllForUserAsync(user.Id, now, ct);
        await refreshTokens.SaveChangesAsync(ct);

        user.UpdatedAt = now;
        user.MfaCredential = null;
        await users.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "user.mfa.reset", "User", user.Id.ToString(), AuditResult.Success,
            currentUser.UserId, currentUser.Username,
            $"username={user.Username} sessionsRevoked=true", ct);

        return ToDto(user);
    }

    /// <summary>
    /// この利用者のほかに有効な管理者が居ることを確かめる。
    /// 居なければ操作を拒否する(誰も設定を変えられない状態を作らない)。
    /// </summary>
    private async Task RequireAnotherActiveAdminAsync(User user, CancellationToken ct)
    {
        var activeAdmins = await users.CountActiveAdminsAsync(ct);
        var othersRemain = user.IsActive ? activeAdmins - 1 : activeAdmins;

        if (othersRemain <= 0)
        {
            throw AppException.BadRequest(
                "last_admin",
                "有効な運用管理者が居なくなるため、この操作はできません。"
                    + "先に別の利用者を運用管理者にしてください。");
        }
    }

    private async Task<User> RequireUserAsync(long userId, CancellationToken ct) =>
        await users.FindByIdAsync(userId, ct)
            ?? throw AppException.NotFound("user_not_found", "利用者が見つかりません。");

    private static UserRole ParseRole(string role) =>
        Enum.TryParse<UserRole>(role, ignoreCase: false, out var parsed)
            ? parsed
            : throw AppException.BadRequest("invalid_role", "役割の指定が不正です。");

    private static ManagedUserDto ToDto(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Role = user.Role.ToString(),
        IsActive = user.IsActive,
        MfaEnabled = user.MfaCredential is { IsEnabled: true },
        CreatedAt = user.CreatedAt,
        UpdatedAt = user.UpdatedAt,
    };
}
