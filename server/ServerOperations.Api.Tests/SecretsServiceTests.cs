using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Api.Services;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;

namespace ServerOperations.Api.Tests;

public class SecretsServiceTests
{
    private readonly FakeEncryptedSecretRepository _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));

    private SecretsService CreateSut() =>
        new(_repo, new EphemeralDataProtectionProvider(), _audit, _currentUser, _time);

    [Fact]
    public async Task Update_StoresEncryptedValue_NotPlaintext()
    {
        var sut = CreateSut();

        var result = await sut.UpdateAsync("gemini-api-key", "super-secret-value");

        Assert.True(result.IsConfigured);
        var stored = Assert.Single(_repo.Secrets);
        Assert.DoesNotContain("super-secret-value", stored.ValueProtected);
    }

    [Fact]
    public async Task Update_WithUnknownKind_Rejects()
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.UpdateAsync("arbitrary-kind", "value"));

        Assert.Equal("unknown_secret_kind", ex.Code);
    }

    [Fact]
    public async Task GetStatus_NeverReturnsValue()
    {
        var sut = CreateSut();
        await sut.UpdateAsync("smtp-password", "super-secret-value");

        var status = await sut.GetStatusAsync("smtp-password");

        Assert.True(status.IsConfigured);
        Assert.NotNull(status.UpdatedAt);
        // DTOに値のプロパティ自体が存在しないことを型で保証しているが、審査のため文字列化しても含まれないことを確認
        Assert.DoesNotContain("super-secret-value", status.ToString());
    }

    [Fact]
    public async Task GetStatus_Unconfigured_ReturnsFalse()
    {
        var sut = CreateSut();

        var status = await sut.GetStatusAsync("smtp-password");

        Assert.False(status.IsConfigured);
        Assert.Null(status.UpdatedAt);
    }

    [Fact]
    public async Task Update_AuditDetails_DoNotContainSecret()
    {
        var sut = CreateSut();

        await sut.UpdateAsync("backup-secret-key", "super-secret-value");

        var entry = Assert.Single(_audit.Entries);
        Assert.Equal("settings.secret.update", entry.Action);
        Assert.DoesNotContain("super-secret-value", entry.ToString());
    }
}
