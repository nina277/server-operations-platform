using Microsoft.Extensions.Options;
using ServerOperations.Api.DTOs.Auth;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class AuthService(
    IUserRepository users,
    IRefreshTokenRepository refreshTokens,
    ITokenService tokenService,
    IMfaService mfaService,
    IAuditService audit,
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider,
    IHttpContextAccessor httpContextAccessor) : IAuthService
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    public async Task<TokenPairResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var user = await users.FindByUsernameAsync(request.Username, ct);

        // ユーザー不存在とパスワード不一致は同じ応答にする(ユーザー列挙を防ぐ)
        if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            await audit.RecordAsync(
                "auth.login", "User", user?.Id.ToString(), AuditResult.Failure,
                actorUserId: user?.Id, actorName: request.Username,
                details: "invalid credentials", ct: ct);
            throw AppException.Unauthorized("invalid_credentials", "ユーザー名またはパスワードが正しくありません。");
        }

        if (!user.IsActive)
        {
            await audit.RecordAsync(
                "auth.login", "User", user.Id.ToString(), AuditResult.Denied,
                actorUserId: user.Id, actorName: user.Username, details: "inactive user", ct: ct);
            throw AppException.Unauthorized("invalid_credentials", "ユーザー名またはパスワードが正しくありません。");
        }

        if (user.MfaCredential is { IsEnabled: true })
        {
            if (string.IsNullOrWhiteSpace(request.TotpCode))
            {
                throw AppException.Unauthorized("mfa_required", "MFAの認証コードを入力してください。");
            }

            if (!await mfaService.ValidateForLoginAsync(user.Id, request.TotpCode, ct))
            {
                await audit.RecordAsync(
                    "auth.login", "User", user.Id.ToString(), AuditResult.Failure,
                    actorUserId: user.Id, actorName: user.Username, details: "invalid TOTP code", ct: ct);
                throw AppException.Unauthorized("mfa_invalid_code", "認証コードが正しくありません。");
            }
        }

        var pair = await IssueTokenPairAsync(user, familyId: Guid.NewGuid(), ct);

        await audit.RecordAsync(
            "auth.login", "User", user.Id.ToString(), AuditResult.Success,
            actorUserId: user.Id, actorName: user.Username, ct: ct);

        return pair;
    }

    public async Task<TokenPairResponse> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tokenHash = tokenService.HashRefreshToken(refreshToken);
        var stored = await refreshTokens.FindByTokenHashAsync(tokenHash, ct);

        if (stored is null)
        {
            await audit.RecordAsync(
                "auth.refresh", "RefreshToken", null, AuditResult.Failure,
                details: "unknown refresh token", ct: ct);
            throw AppException.Unauthorized("invalid_refresh_token", "リフレッシュトークンが無効です。");
        }

        // 失効済みトークンの再利用 = 漏えいの疑い。同一系列をすべて失効させる。
        if (stored.RevokedAt is not null)
        {
            await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, ct);
            await refreshTokens.SaveChangesAsync(ct);
            await audit.RecordAsync(
                "auth.refresh.reuse_detected", "RefreshToken", stored.Id.ToString(), AuditResult.Denied,
                actorUserId: stored.UserId, actorName: stored.User?.Username,
                details: $"revoked token reuse detected; family {stored.FamilyId} revoked", ct: ct);
            throw AppException.Unauthorized("invalid_refresh_token", "リフレッシュトークンが無効です。");
        }

        if (stored.IsExpired(now) || stored.User is null || !stored.User.IsActive)
        {
            await audit.RecordAsync(
                "auth.refresh", "RefreshToken", stored.Id.ToString(), AuditResult.Failure,
                actorUserId: stored.UserId, actorName: stored.User?.Username,
                details: "expired token or inactive user", ct: ct);
            throw AppException.Unauthorized("invalid_refresh_token", "リフレッシュトークンが無効です。");
        }

        // ローテーション: 旧トークンを失効させ、同一系列で新トークンを発行する
        var pair = await IssueTokenPairAsync(stored.User, stored.FamilyId, ct, beforeSave: newTokenHash =>
        {
            stored.RevokedAt = now;
            stored.ReplacedByTokenHash = newTokenHash;
        });

        await audit.RecordAsync(
            "auth.refresh", "User", stored.UserId.ToString(), AuditResult.Success,
            actorUserId: stored.UserId, actorName: stored.User.Username, ct: ct);

        return pair;
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var tokenHash = tokenService.HashRefreshToken(refreshToken);
        var stored = await refreshTokens.FindByTokenHashAsync(tokenHash, ct);

        // 不明なトークンでも成功として扱う(ログアウトは冪等)
        if (stored is null)
        {
            return;
        }

        await refreshTokens.RevokeFamilyAsync(stored.FamilyId, now, ct);
        await refreshTokens.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "auth.logout", "User", stored.UserId.ToString(), AuditResult.Success,
            actorUserId: stored.UserId, actorName: stored.User?.Username, ct: ct);
    }

    public async Task<MeResponse> GetMeAsync(long userId, CancellationToken ct = default)
    {
        var user = await users.FindByIdAsync(userId, ct)
            ?? throw AppException.NotFound("user_not_found", "ユーザーが見つかりません。");

        return new MeResponse
        {
            Id = user.Id,
            Username = user.Username,
            Role = user.Role.ToString(),
            MfaEnabled = user.MfaCredential is { IsEnabled: true },
        };
    }

    private async Task<TokenPairResponse> IssueTokenPairAsync(
        User user, Guid familyId, CancellationToken ct, Action<string>? beforeSave = null)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var access = tokenService.CreateAccessToken(user);

        var refreshValue = tokenService.GenerateRefreshTokenValue();
        var refreshHash = tokenService.HashRefreshToken(refreshValue);
        var refreshExpiresAt = now.AddDays(_jwt.RefreshTokenDays);

        beforeSave?.Invoke(refreshHash);

        await refreshTokens.AddAsync(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = refreshHash,
            FamilyId = familyId,
            ExpiresAt = refreshExpiresAt,
            CreatedAt = now,
            CreatedByIp = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString(),
        }, ct);
        await refreshTokens.SaveChangesAsync(ct);

        return new TokenPairResponse
        {
            AccessToken = access.Token,
            AccessTokenExpiresAt = access.ExpiresAt,
            RefreshToken = refreshValue,
            RefreshTokenExpiresAt = refreshExpiresAt,
        };
    }
}
