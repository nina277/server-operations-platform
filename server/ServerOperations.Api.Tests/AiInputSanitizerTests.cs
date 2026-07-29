using ServerOperations.Core.Services.Ai;

namespace ServerOperations.Api.Tests;

public class AiInputSanitizerTests
{
    [Theory]
    [InlineData("connection refused to 192.168.1.10:3306", "192.168.1.10")]
    [InlineData("upstream 10.0.0.5 timed out", "10.0.0.5")]
    public void Anonymize_RemovesIpv4(string input, string ip)
    {
        var result = AiInputSanitizer.Anonymize(input);

        Assert.DoesNotContain(ip, result);
        Assert.Contains("<IP>", result);
    }

    [Fact]
    public void Anonymize_RemovesIpv6()
    {
        var result = AiInputSanitizer.Anonymize("peer fe80::1ff:fe23:4567:890a unreachable");

        Assert.DoesNotContain("fe80::1ff", result);
    }

    [Theory]
    [InlineData("host db01.internal not found", "db01.internal")]
    [InlineData("cannot resolve nas.local", "nas.local")]
    [InlineData("timeout to gateway.lan", "gateway.lan")]
    public void Anonymize_RemovesInternalHostnames(string input, string host)
    {
        var result = AiInputSanitizer.Anonymize(input);

        Assert.DoesNotContain(host, result);
        Assert.Contains("<HOST>", result);
    }

    [Fact]
    public void Anonymize_RemovesEmail()
    {
        var result = AiInputSanitizer.Anonymize("notify admin@example.com failed");

        Assert.DoesNotContain("admin@example.com", result);
        Assert.Contains("<EMAIL>", result);
    }

    [Fact]
    public void Anonymize_RemovesJwt()
    {
        const string jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijk";

        var result = AiInputSanitizer.Anonymize($"auth failed for {jwt}");

        Assert.DoesNotContain("eyJzdWIiOiIxMjM0NTY3ODkwIn0", result);
    }

    [Fact]
    public void Anonymize_RemovesCookie()
    {
        var result = AiInputSanitizer.Anonymize("request had Cookie: session=abc123def456");

        Assert.DoesNotContain("abc123def456", result);
    }

    [Fact]
    public void Anonymize_RemovesPasswordAndToken()
    {
        var result = AiInputSanitizer.Anonymize("db password=hunter2 api_key=xyz987");

        Assert.DoesNotContain("hunter2", result);
        Assert.DoesNotContain("xyz987", result);
    }

    [Fact]
    public void Anonymize_RemovesLongRandomStrings()
    {
        const string apiKey = "AIzaSyC1234567890abcdefghijklmnopqrstuvw";

        var result = AiInputSanitizer.Anonymize($"using key {apiKey}");

        Assert.DoesNotContain(apiKey, result);
        Assert.Contains("<TOKEN>", result);
    }

    [Fact]
    public void Anonymize_KeepsUsefulDiagnosticText()
    {
        const string input = "ERROR container exited with code 137 out of memory";

        var result = AiInputSanitizer.Anonymize(input);

        // 診断に必要な情報は残す
        Assert.Contains("exited with code", result);
        Assert.Contains("out of memory", result);
    }

    [Fact]
    public void Reduce_RemovesDuplicateLines()
    {
        const string input = "same error\nsame error\nsame error\nother error";

        var result = AiInputSanitizer.Reduce(input, 1000);

        Assert.Equal("same error\nother error", result);
    }

    [Fact]
    public void Reduce_TruncatesToMaxCharacters()
    {
        var input = string.Join('\n', Enumerable.Range(0, 200).Select(i => $"line {i}"));

        var result = AiInputSanitizer.Reduce(input, 100);

        Assert.True(result.Length <= 100);
    }

    [Fact]
    public void Prepare_AppliesAnonymizeThenReduce()
    {
        const string input = "error at 192.168.1.10\nerror at 192.168.1.10\npassword=secret";

        var result = AiInputSanitizer.Prepare(input, 1000);

        Assert.DoesNotContain("192.168.1.10", result);
        Assert.DoesNotContain("secret", result);
        // 匿名化後に同一となった行は1行へ圧縮される
        Assert.Equal(2, result.Split('\n').Length);
    }

    [Fact]
    public void Prepare_EmptyInput_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, AiInputSanitizer.Prepare(null, 1000));
        Assert.Equal(string.Empty, AiInputSanitizer.Prepare(string.Empty, 1000));
    }
}
