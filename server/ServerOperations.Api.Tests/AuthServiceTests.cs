using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using ServerOperations.Api.DTOs.Auth;
using ServerOperations.Api.Extensions;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;

namespace ServerOperations.Api.Tests;

public class AuthServiceTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeUserRepository _users = new();
    private readonly FakeRefreshTokenRepository _refreshTokens;

    public AuthServiceTests()
    {
        _refreshTokens = new FakeRefreshTokenRepository(_users);
    }
    private readonly FakeAuditService _audit = new();
    private readonly FakeMfaService _mfa = new();
    private readonly TestTimeProvider _time = new(BaseTime);

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
            _users, _refreshTokens, tokenService, _mfa, _audit, options, _time, accessor);
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
}
