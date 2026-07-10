using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Tests.Fakes;

namespace ServerOperations.Api.Tests;

public class RecentMfaHandlerTests
{
    private static readonly DateTimeOffset BaseTime = new(2026, 7, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeMfaCredentialRepository _credentials = new();
    private readonly TestTimeProvider _time = new(BaseTime);

    private RecentMfaHandler CreateSut() =>
        new(_credentials, Options.Create(new JwtOptions { MfaFreshnessMinutes = 15 }), _time);

    private static AuthorizationHandlerContext CreateContext(long userId)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "TestAuth");
        return new AuthorizationHandlerContext(
            [new RecentMfaRequirement()], new ClaimsPrincipal(identity), resource: null);
    }

    [Fact]
    public async Task Succeeds_WhenMfaVerifiedWithinFreshnessWindow()
    {
        _credentials.Credentials.Add(new MfaCredential
        {
            UserId = 1,
            SecretProtected = "protected",
            IsEnabled = true,
            LastVerifiedAt = BaseTime.UtcDateTime.AddMinutes(-5),
        });
        var context = CreateContext(1);

        await CreateSut().HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_WhenMfaVerificationIsStale()
    {
        _credentials.Credentials.Add(new MfaCredential
        {
            UserId = 1,
            SecretProtected = "protected",
            IsEnabled = true,
            LastVerifiedAt = BaseTime.UtcDateTime.AddMinutes(-30),
        });
        var context = CreateContext(1);

        await CreateSut().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Fails_WhenMfaIsNotEnabled()
    {
        var context = CreateContext(1);

        await CreateSut().HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }
}
