using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServerOperations.Api.Tests;

public class AuthEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/v1/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MfaSetup_WithoutToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync("/api/v1/auth/mfa/setup", content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/api/v1/settings/profile")]
    [InlineData("/api/v1/settings/retention")]
    [InlineData("/api/v1/settings/network-cidrs")]
    [InlineData("/api/v1/settings/secrets/smtp-password/status")]
    public async Task SettingsEndpoints_WithoutToken_Return401(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithMalformedBody_ReturnsApiResponseValidationError()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("\"success\":false", body);
        Assert.Contains("validation_error", body);
    }
}
