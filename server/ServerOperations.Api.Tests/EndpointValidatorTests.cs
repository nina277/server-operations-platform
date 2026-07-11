using ServerOperations.Api.Adapters.Implementations;
using ServerOperations.Api.Services;

namespace ServerOperations.Api.Tests;

public class EndpointValidatorTests
{
    [Theory]
    [InlineData("http://127.0.0.1/health")]
    [InlineData("http://localhost/health")]
    [InlineData("http://[::1]/health")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://169.254.10.20/health")]
    [InlineData("http://224.0.0.1/health")]
    [InlineData("http://0.0.0.0/health")]
    [InlineData("http://255.255.255.255/health")]
    public async Task ValidateHttpUrl_BlockedAddresses_Rejected(string url)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            EndpointValidator.ValidateHttpUrlAsync(url));

        Assert.Equal("url_not_allowed", ex.Code);
    }

    [Theory]
    [InlineData("ftp://192.168.1.10/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("gopher://192.168.1.10/")]
    public async Task ValidateHttpUrl_DisallowedSchemes_Rejected(string url)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            EndpointValidator.ValidateHttpUrlAsync(url));

        Assert.Equal("invalid_url_scheme", ex.Code);
    }

    [Fact]
    public async Task ValidateHttpUrl_MalformedUrl_Rejected()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            EndpointValidator.ValidateHttpUrlAsync("not a url"));

        Assert.Equal("invalid_url", ex.Code);
    }

    [Theory]
    [InlineData("http://192.168.1.10/health")]
    [InlineData("https://10.0.0.5:8443/api/health")]
    public async Task ValidateHttpUrl_PrivateLanAddresses_Allowed(string url)
    {
        // 自宅サーバー監視が目的のため、プライベートアドレスは許可される
        await EndpointValidator.ValidateHttpUrlAsync(url);
    }

    [Theory]
    [InlineData("unix:///var/run/docker.sock")]
    [InlineData("npipe:////./pipe/docker_engine")]
    [InlineData("tcp://192.168.1.10:2375")]
    public async Task ValidateDockerEndpoint_NonHttpSchemes_Rejected(string endpoint)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            EndpointValidator.ValidateDockerEndpointAsync(endpoint));

        Assert.Equal("invalid_url_scheme", ex.Code);
    }

    [Theory]
    [InlineData("http://192.168.1.10:2375")]
    [InlineData("https://192.168.1.10:2376")]
    public async Task ValidateDockerEndpoint_SocketProxyOrTls_Allowed(string endpoint)
    {
        await EndpointValidator.ValidateDockerEndpointAsync(endpoint);
    }

    [Fact]
    public async Task ValidateDockerEndpoint_Loopback_Rejected()
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            EndpointValidator.ValidateDockerEndpointAsync("http://127.0.0.1:2375"));

        Assert.Equal("url_not_allowed", ex.Code);
    }

    [Fact]
    public void IsBlockedAddress_HandlesIpv4MappedIpv6()
    {
        Assert.True(EndpointValidator.IsBlockedAddress(System.Net.IPAddress.Parse("::ffff:127.0.0.1")));
        Assert.False(EndpointValidator.IsBlockedAddress(System.Net.IPAddress.Parse("::ffff:192.168.1.10")));
    }

    [Theory]
    [InlineData("http://user:secret@192.168.1.10/health")]
    [InlineData("https://token@10.0.0.5/api")]
    public async Task ValidateHttpUrl_EmbeddedCredentials_Rejected(string url)
    {
        var ex = await Assert.ThrowsAsync<AppException>(() =>
            EndpointValidator.ValidateHttpUrlAsync(url));

        Assert.Equal("credentials_in_url", ex.Code);
    }

    [Fact]
    public async Task ResolveAllowedAddresses_BlockedLiteral_ThrowsHttpRequestException()
    {
        // 接続時ガード: 遮断対象しか解決できないホストへは接続させない
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            EndpointValidator.ResolveAllowedAddressesAsync("127.0.0.1", CancellationToken.None));
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            EndpointValidator.ResolveAllowedAddressesAsync("localhost", CancellationToken.None));
    }

    [Fact]
    public async Task ResolveAllowedAddresses_AllowedLiteral_ReturnsAddress()
    {
        var addresses = await EndpointValidator.ResolveAllowedAddressesAsync(
            "192.168.1.20", CancellationToken.None);

        Assert.Single(addresses);
    }
}
