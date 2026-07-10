using System.Net;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Models.Settings;
using ServerOperations.Api.Services;
using ServerOperations.Api.Services.Implementations;
using ServerOperations.Api.Tests.Fakes;

namespace ServerOperations.Api.Tests;

public class NetworkCidrServiceTests
{
    private readonly FakeTrustedNetworkCidrRepository _repo = new();
    private readonly FakeAuditService _audit = new();
    private readonly FakeCurrentUserAccessor _currentUser = new();
    private readonly TestTimeProvider _time = new(new DateTimeOffset(2026, 7, 10, 12, 0, 0, TimeSpan.Zero));

    private NetworkCidrService CreateSut() => new(_repo, _audit, _currentUser, _time);

    [Theory]
    [InlineData("not-a-cidr")]
    [InlineData("192.168.1.0")]
    [InlineData("192.168.1.0/33")]
    [InlineData("192.168.1.0/-1")]
    [InlineData("999.0.0.1/24")]
    public async Task Add_WithInvalidFormat_Rejects(string cidr)
    {
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.AddAsync(new CreateNetworkCidrRequest { Cidr = cidr }));

        Assert.Equal("invalid_cidr", ex.Code);
    }

    [Fact]
    public async Task Add_NormalizesHostBits()
    {
        // 現在の接続元(192.168.1.10)を含む範囲
        var sut = CreateSut();

        var result = await sut.AddAsync(new CreateNetworkCidrRequest { Cidr = "192.168.1.5/24" });

        Assert.Equal("192.168.1.0/24", result.Cidr);
    }

    [Fact]
    public async Task Add_Duplicate_Rejects()
    {
        _repo.Cidrs.Add(new TrustedNetworkCidr { Id = 1, Cidr = "192.168.1.0/24" });
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.AddAsync(new CreateNetworkCidrRequest { Cidr = "192.168.1.0/24" }));

        Assert.Equal("duplicate_cidr", ex.Code);
    }

    [Fact]
    public async Task Add_FirstCidrExcludingCurrentIp_RejectsLockout()
    {
        _currentUser.RemoteIp = IPAddress.Parse("10.0.0.5");
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            sut.AddAsync(new CreateNetworkCidrRequest { Cidr = "192.168.1.0/24" }));

        Assert.Equal("cidr_would_lock_out", ex.Code);
        Assert.Empty(_repo.Cidrs);
    }

    [Fact]
    public async Task Delete_LastCidr_Rejects()
    {
        _repo.Cidrs.Add(new TrustedNetworkCidr { Id = 1, Cidr = "192.168.1.0/24" });
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.DeleteAsync(1));

        Assert.Equal("cannot_delete_last_cidr", ex.Code);
        Assert.Single(_repo.Cidrs);
    }

    [Fact]
    public async Task Delete_CausingLockout_Rejects()
    {
        // 現在の接続元(192.168.1.10)を含むのはId=1だけ
        _repo.Cidrs.Add(new TrustedNetworkCidr { Id = 1, Cidr = "192.168.1.0/24" });
        _repo.Cidrs.Add(new TrustedNetworkCidr { Id = 2, Cidr = "10.0.0.0/8" });
        var sut = CreateSut();

        var ex = await Assert.ThrowsAsync<AppException>(() => sut.DeleteAsync(1));

        Assert.Equal("cidr_would_lock_out", ex.Code);
        Assert.Equal(2, _repo.Cidrs.Count);
    }

    [Fact]
    public async Task Delete_SafeCidr_SucceedsAndAudits()
    {
        _repo.Cidrs.Add(new TrustedNetworkCidr { Id = 1, Cidr = "192.168.1.0/24" });
        _repo.Cidrs.Add(new TrustedNetworkCidr { Id = 2, Cidr = "10.0.0.0/8" });
        var sut = CreateSut();

        await sut.DeleteAsync(2);

        Assert.Single(_repo.Cidrs);
        Assert.Contains(_audit.Entries, e => e.Action == "settings.network_cidr.delete");
    }

    [Fact]
    public async Task IsAllowed_WithNoCidrs_AllowsAll()
    {
        var sut = CreateSut();
        Assert.True(await sut.IsAllowedAsync(IPAddress.Parse("203.0.113.9")));
    }

    [Fact]
    public async Task IsAllowed_EnforcesRangesWhenConfigured()
    {
        _repo.Cidrs.Add(new TrustedNetworkCidr { Id = 1, Cidr = "192.168.1.0/24" });
        var sut = CreateSut();

        Assert.True(await sut.IsAllowedAsync(IPAddress.Parse("192.168.1.200")));
        Assert.False(await sut.IsAllowedAsync(IPAddress.Parse("203.0.113.9")));
        Assert.False(await sut.IsAllowedAsync(null));
    }

    [Fact]
    public async Task IsAllowed_HandlesIpv4MappedIpv6()
    {
        _repo.Cidrs.Add(new TrustedNetworkCidr { Id = 1, Cidr = "192.168.1.0/24" });
        var sut = CreateSut();

        Assert.True(await sut.IsAllowedAsync(IPAddress.Parse("::ffff:192.168.1.10")));
    }
}
