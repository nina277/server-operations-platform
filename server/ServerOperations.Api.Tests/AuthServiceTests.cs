using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ServerOperations.Api.DTOs.Auth;
using ServerOperations.Api.Extensions;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Services;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;

namespace ServerOperations.Api.Tests;

public class AuthServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _users = new();
    private readonly FakeRefreshTokenRepository _refreshTokens;

    private readonly FakeAuditService _audit = new();
    private readonly FakeMfaService _mfa = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    /// <summary>制限そのものは LoginThrottleTests で確かめる。ここでは同じ時計を共有する。</summary>
    private readonly LoginThrottle _throttle;

    public AuthServiceTests()
    {
        _refreshTokens = new FakeRefreshTokenRepository(_users);
        _throttle = new LoginThrottle(_time);
    }

    private AuthService CreateSut()
    {
        var options = Options.Create(new JwtOptions
        {
            SigningKey = new string('k', 48),
            AccessTokenMinutes = 60,
            RefreshTokenDays = 30,
        });

        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var tokenService = new JwtTokenService(options, _time);

        return new AuthService(
            _users, _refreshTokens, tokenService, _mfa, _audit, options, _throttle, _time, accessor);
    }

    private User AddUser(string username = "admin", string password = "correct-password", bool mfaEnabled = false)
    {
        var user = new User
        {
            Id = 1,
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRole.OperatorAdmin,
            IsActive = true,
        };
        if (mfaEnabled)
        {
            user.MfaCredential = new MfaCredential
            {
                UserId = 1,
                SecretProtected = "protected",
                IsEnabled = true,
            };
        }

        _users.Users.Add(user);
        return user;
    }

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsTokenPair()
    {
        AddUser();
        var sut = CreateSut();

        var pair = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" });

        Assert.NotEmpty(pair.AccessToken);
        Assert.NotEmpty(pair.RefreshToken);
        Assert.Single(_refreshTokens.Tokens);
        Assert.Contains(_audit.Entries, e => e.Action == "auth.login" && e.Result == AuditResult.Success);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ThrowsAndAuditsFailure()
    {
        AddUser();
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }));

        Assert.Equal(StatusCodes.Status401Unauthorized, ex.StatusCode);
        Assert.Equal("invalid_credentials", ex.Code);
        Assert.Contains(_audit.Entries, e => e.Action == "auth.login" && e.Result == AuditResult.Failure);
        Assert.Empty(_refreshTokens.Tokens);
    }

    [Fact]
    public async Task Login_WithUnknownUser_ReturnsSameErrorAsWrongPassword()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.LoginAsync(new LoginRequest { Username = "nobody", Password = "whatever" }));

        Assert.Equal("invalid_credentials", ex.Code);
    }

    [Fact]
    public async Task Login_WithMfaEnabledAndNoCode_RequiresMfa()
    {
        AddUser(mfaEnabled: true);
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" }));

        Assert.Equal("mfa_required", ex.Code);
    }

    [Fact]
    public async Task Login_WithMfaEnabledAndInvalidCode_ThrowsAndAudits()
    {
        AddUser(mfaEnabled: true);
        _mfa.ValidateResult = false;
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.LoginAsync(new LoginRequest
            {
                Username = "admin",
                Password = "correct-password",
                TotpCode = "000000",
            }));

        Assert.Equal("mfa_invalid_code", ex.Code);
        Assert.Contains(_audit.Entries, e => e.Action == "auth.login" && e.Result == AuditResult.Failure);
    }

    [Fact]
    public async Task Refresh_WithValidToken_RotatesWithinSameFamily()
    {
        var user = AddUser();
        var sut = CreateSut();
        var pair = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" });
        var firstToken = _refreshTokens.Tokens.Single();

        var newPair = await sut.RefreshAsync(pair.RefreshToken);

        Assert.NotEqual(pair.RefreshToken, newPair.RefreshToken);
        Assert.Equal(2, _refreshTokens.Tokens.Count);

        var secondToken = _refreshTokens.Tokens[1];
        Assert.Equal(firstToken.FamilyId, secondToken.FamilyId);
        Assert.NotNull(firstToken.RevokedAt);
        Assert.Equal(secondToken.TokenHash, firstToken.ReplacedByTokenHash);
        Assert.Null(secondToken.RevokedAt);
        Assert.Equal(user.Id, secondToken.UserId);
    }

    [Fact]
    public async Task Refresh_WithReusedRevokedToken_RevokesEntireFamily()
    {
        AddUser();
        var sut = CreateSut();
        var pair = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" });

        // 正常なローテーション後、旧トークンを再利用する(漏えいシナリオ)
        await sut.RefreshAsync(pair.RefreshToken);
        var ex = await Assert.ThrowsAsync<AppException>(() => sut.RefreshAsync(pair.RefreshToken));

        Assert.Equal("invalid_refresh_token", ex.Code);
        Assert.All(_refreshTokens.Tokens, t => Assert.NotNull(t.RevokedAt));
        Assert.Contains(_audit.Entries,
            e => e.Action == "auth.refresh.reuse_detected" && e.Result == AuditResult.Denied);
    }

    [Fact]
    public async Task Refresh_WithExpiredToken_Throws()
    {
        AddUser();
        var sut = CreateSut();
        var pair = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" });

        _time.Now = BaseTime.AddDays(31);

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.RefreshAsync(pair.RefreshToken));
        Assert.Equal("invalid_refresh_token", ex.Code);
    }

    [Fact]
    public async Task Logout_RevokesTokenFamily()
    {
        AddUser();
        var sut = CreateSut();
        var pair = await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" });

        await sut.LogoutAsync(pair.RefreshToken);

        Assert.All(_refreshTokens.Tokens, t => Assert.NotNull(t.RevokedAt));
        Assert.Contains(_audit.Entries, e => e.Action == "auth.logout" && e.Result == AuditResult.Success);
    }

    [Fact]
    public async Task Logout_WithUnknownToken_IsIdempotent()
    {
        var sut = CreateSut();
        await sut.LogoutAsync("unknown-token");
        Assert.Empty(_audit.Entries);
    }

    [Fact]
    public void PasswordHash_IsBCryptFormat()
    {
        var user = AddUser();
        Assert.StartsWith("$2", user.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify("correct-password", user.PasswordHash));
    }

    // --- パスワード変更 ---

    [Fact]
    public async Task パスワードを変更できる()
    {
        var user = AddUser(password: "current-password-1");

        var result = await CreateSut().ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "current-password-1",
            NewPassword = "brand-new-password-1",
        });

        Assert.True(result.OtherSessionsRevoked);
        Assert.True(BCrypt.Net.BCrypt.Verify("brand-new-password-1", user.PasswordHash));
    }

    [Fact]
    public async Task 現在のパスワードが違えば変更できない()
    {
        var user = AddUser(password: "current-password-1");
        var originalHash = user.PasswordHash;

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().ChangePasswordAsync(user.Id, new ChangePasswordRequest
            {
                CurrentPassword = "wrong-password",
                NewPassword = "brand-new-password-1",
            }));

        Assert.Equal("invalid_current_password", ex.Code);
        Assert.Equal(originalHash, user.PasswordHash);
    }

    [Fact]
    public async Task 現在のパスワードが違う試行を監査に残す()
    {
        var user = AddUser(password: "current-password-1");

        await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().ChangePasswordAsync(user.Id, new ChangePasswordRequest
            {
                CurrentPassword = "wrong-password",
                NewPassword = "brand-new-password-1",
            }));

        Assert.Contains(_audit.Entries,
            e => e.Action == "auth.password.change" && e.Result == AuditResult.Failure);
    }

    [Fact]
    public async Task 同じパスワードへの変更は受け付けない()
    {
        var user = AddUser(password: "current-password-1");

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            CreateSut().ChangePasswordAsync(user.Id, new ChangePasswordRequest
            {
                CurrentPassword = "current-password-1",
                NewPassword = "current-password-1",
            }));

        Assert.Equal("password_unchanged", ex.Code);
    }

    [Fact]
    public async Task 変更したら他の端末のセッションを失効させる()
    {
        // 漏えい後の変更で古いセッションが残らないようにする
        var user = AddUser(password: "current-password-1");
        var sut = CreateSut();
        await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "current-password-1" });
        await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "current-password-1" });

        Assert.Equal(2, _refreshTokens.Tokens.Count(t => t.RevokedAt is null));

        await sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "current-password-1",
            NewPassword = "brand-new-password-1",
        });

        Assert.DoesNotContain(_refreshTokens.Tokens, t => t.RevokedAt is null);
    }

    [Fact]
    public async Task 変更後は古いリフレッシュトークンで更新できない()
    {
        var user = AddUser(password: "current-password-1");
        var sut = CreateSut();
        var pair = await sut.LoginAsync(
            new LoginRequest { Username = "admin", Password = "current-password-1" });

        await sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "current-password-1",
            NewPassword = "brand-new-password-1",
        });

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.RefreshAsync(pair.RefreshToken));
        Assert.Equal("invalid_refresh_token", ex.Code);
    }

    [Fact]
    public async Task 変更後は新しいパスワードでログインできる()
    {
        var user = AddUser(password: "current-password-1");
        var sut = CreateSut();

        await sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "current-password-1",
            NewPassword = "brand-new-password-1",
        });

        var pair = await sut.LoginAsync(
            new LoginRequest { Username = "admin", Password = "brand-new-password-1" });

        Assert.NotEmpty(pair.AccessToken);
    }

    [Fact]
    public async Task 変更後は古いパスワードでログインできない()
    {
        var user = AddUser(password: "current-password-1");
        var sut = CreateSut();

        await sut.ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "current-password-1",
            NewPassword = "brand-new-password-1",
        });

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.LoginAsync(
            new LoginRequest { Username = "admin", Password = "current-password-1" }));

        Assert.Equal("invalid_credentials", ex.Code);
    }

    [Fact]
    public async Task 監査にパスワードそのものを残さない()
    {
        var user = AddUser(password: "current-password-1");

        await CreateSut().ChangePasswordAsync(user.Id, new ChangePasswordRequest
        {
            CurrentPassword = "current-password-1",
            NewPassword = "brand-new-password-1",
        });

        foreach (var entry in _audit.Entries)
        {
            Assert.DoesNotContain("current-password-1", entry.Details ?? string.Empty);
            Assert.DoesNotContain("brand-new-password-1", entry.Details ?? string.Empty);
        }
    }

    // --- ログイン試行の制限 ---

    [Fact]
    public async Task 失敗が続いたら受け付けなくなる()
    {
        AddUser();
        var sut = CreateSut();

        for (var i = 0; i <= LoginThrottle.MaxFailuresPerUser; i++)
        {
            await Assert.ThrowsAsync<AppException>(() =>
                sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }));
        }

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }));

        Assert.Equal(StatusCodes.Status429TooManyRequests, ex.StatusCode);
        Assert.Equal("too_many_attempts", ex.Code);
    }

    [Fact]
    public async Task 遮断中は正しいパスワードでも受け付けない()
    {
        // 判定を認証情報より先に行う。後にすると、止まるまでの時間の差で
        // 利用者名の存在を推測できてしまう。
        AddUser();
        var sut = CreateSut();

        for (var i = 0; i <= LoginThrottle.MaxFailuresPerUser; i++)
        {
            await Assert.ThrowsAsync<AppException>(() =>
                sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }));
        }

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" }));

        Assert.Equal("too_many_attempts", ex.Code);
    }

    [Fact]
    public async Task 遮断を監査に残す()
    {
        AddUser();
        var sut = CreateSut();

        for (var i = 0; i <= LoginThrottle.MaxFailuresPerUser; i++)
        {
            await Assert.ThrowsAsync<AppException>(() =>
                sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }));
        }

        await Assert.ThrowsAsync<AppException>(() =>
            sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }));

        Assert.Contains(_audit.Entries,
            e => e.Action == "auth.login.throttled" && e.Result == AuditResult.Denied);
    }

    [Fact]
    public async Task 遮断の監査にパスワードを載せない()
    {
        AddUser();
        var sut = CreateSut();

        for (var i = 0; i <= LoginThrottle.MaxFailuresPerUser + 1; i++)
        {
            await Assert.ThrowsAsync<AppException>(() =>
                sut.LoginAsync(new LoginRequest { Username = "admin", Password = "secret-guess" }));
        }

        Assert.All(_audit.Entries,
            e => Assert.DoesNotContain("secret-guess", e.Details ?? string.Empty));
    }

    [Fact]
    public async Task 待ち時間が過ぎれば再び受け付ける()
    {
        AddUser();
        var sut = CreateSut();

        for (var i = 0; i <= LoginThrottle.MaxFailuresPerUser; i++)
        {
            await Assert.ThrowsAsync<AppException>(() =>
                sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }));
        }

        _time.Advance(TimeSpan.FromMinutes(2));
        var pair = await sut.LoginAsync(
            new LoginRequest { Username = "admin", Password = "correct-password" });

        Assert.NotEmpty(pair.AccessToken);
    }

    [Fact]
    public async Task 成功したら数え直す()
    {
        AddUser();
        var sut = CreateSut();

        for (var i = 0; i < LoginThrottle.MaxFailuresPerUser; i++)
        {
            await Assert.ThrowsAsync<AppException>(() =>
                sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }));
        }

        await sut.LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" });

        // 数え直されているので、もう一度失敗しても遮断されない
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.LoginAsync(new LoginRequest { Username = "admin", Password = "wrong" }));
        Assert.Equal("invalid_credentials", ex.Code);
    }

    [Fact]
    public async Task 存在しない利用者名でも同じ回数で遮断する()
    {
        // 存在する名前だけ遮断されると、応答の違いで存在が分かる
        var sut = CreateSut();

        for (var i = 0; i <= LoginThrottle.MaxFailuresPerUser; i++)
        {
            await Assert.ThrowsAsync<AppException>(() =>
                sut.LoginAsync(new LoginRequest { Username = "ghost", Password = "wrong" }));
        }

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.LoginAsync(new LoginRequest { Username = "ghost", Password = "wrong" }));

        Assert.Equal("too_many_attempts", ex.Code);
    }

    [Fact]
    public async Task MFAコードの誤りも回数に数える()
    {
        // 6桁のコードは総当たりが現実的なため、数えないと意味がない
        AddUser(mfaEnabled: true);
        _mfa.ValidateResult = false;
        var sut = CreateSut();

        for (var i = 0; i <= LoginThrottle.MaxFailuresPerUser; i++)
        {
            await Assert.ThrowsAsync<AppException>(() => sut.LoginAsync(
                new LoginRequest { Username = "admin", Password = "correct-password", TotpCode = "000000" }));
        }

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.LoginAsync(
            new LoginRequest { Username = "admin", Password = "correct-password", TotpCode = "000000" }));

        Assert.Equal("too_many_attempts", ex.Code);
    }

    [Fact]
    public async Task MFAコード未入力は回数に数えない()
    {
        // 入力の途中であり、正しいパスワードを持つ本人を締め出さない
        AddUser(mfaEnabled: true);
        var sut = CreateSut();

        for (var i = 0; i < LoginThrottle.MaxFailuresPerUser + 5; i++)
        {
            var pending = await Assert.ThrowsAsync<AppException>(() =>
                sut.LoginAsync(new LoginRequest { Username = "admin", Password = "correct-password" }));
            Assert.Equal("mfa_required", pending.Code);
        }
    }
}
