using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Adapters.Implementations;

namespace ServerOperations.Api.Tests;

public class DockerAdapterTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responder(request));
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static DockerAdapter CreateSut(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        new(new StubFactory(new StubHandler(responder)), NullLogger<DockerAdapter>.Instance);

    [Fact]
    public async Task ValidVersionResponse_ReturnsSuccess()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"Version":"27.0.3","ApiVersion":"1.47"}""", Encoding.UTF8, "application/json"),
        });

        var result = await sut.TestConnectionAsync("http://192.168.1.20:2375");

        Assert.True(result.Success);
        Assert.Contains("27.0.3", result.Detail);
    }

    [Fact]
    public async Task NonJsonResponse_ReturnsFailure_NotException()
    {
        // リバースプロキシのHTMLエラーページ等が200で返るケース
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>It works!</html>", Encoding.UTF8, "text/html"),
        });

        var result = await sut.TestConnectionAsync("http://192.168.1.20:2375");

        Assert.False(result.Success);
        Assert.Contains("Docker API", result.Message);
    }

    [Fact]
    public async Task ErrorStatus_ReturnsFailure()
    {
        var sut = CreateSut(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var result = await sut.TestConnectionAsync("http://192.168.1.20:2375");

        Assert.False(result.Success);
        Assert.Contains("403", result.Message);
    }
}
