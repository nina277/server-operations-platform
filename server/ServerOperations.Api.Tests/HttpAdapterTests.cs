using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Adapters.Implementations;
using ServerOperations.Api.Adapters.Interfaces;

namespace ServerOperations.Api.Tests;

public class HttpAdapterTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static HttpAdapter CreateSut(StubHandler handler) =>
        new(new StubFactory(handler), NullLogger<HttpAdapter>.Instance);

    [Fact]
    public async Task ExpectedStatus_ReturnsSuccess()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        var result = await sut.TestConnectionAsync(new HttpCheckOptions
        {
            Url = "http://192.168.1.10/health",
        });

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Redirect_IsNotFollowed_AndReportsFailure()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
        var handler = new StubHandler(_ => response);
        var sut = CreateSut(handler);

        var result = await sut.TestConnectionAsync(new HttpCheckOptions
        {
            Url = "http://192.168.1.10/health",
        });

        Assert.False(result.Success);
        Assert.Contains("リダイレクト", result.Message);
        // StubHandlerは1回しか呼ばれない=リダイレクト先へ接続していない
        Assert.Equal(new Uri("http://192.168.1.10/health"), handler.LastRequest!.RequestUri);
    }

    [Fact]
    public async Task UnexpectedStatus_ReportsFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var sut = CreateSut(handler);

        var result = await sut.TestConnectionAsync(new HttpCheckOptions
        {
            Url = "http://192.168.1.10/health",
            ExpectedStatus = 200,
        });

        Assert.False(result.Success);
        Assert.Contains("503", result.Message);
    }

    [Fact]
    public async Task BasicAuth_SetsAuthorizationHeader()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var sut = CreateSut(handler);

        await sut.TestConnectionAsync(new HttpCheckOptions
        {
            Url = "http://192.168.1.10/health",
            BasicAuthUser = "user",
            BasicAuthPassword = "pass",
        });

        Assert.Equal("Basic", handler.LastRequest!.Headers.Authorization!.Scheme);
    }
}
