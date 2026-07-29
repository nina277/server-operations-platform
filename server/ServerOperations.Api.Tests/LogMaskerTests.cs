using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

public class LogMaskerTests
{
    [Theory]
    [InlineData("password=hunter2", "hunter2")]
    [InlineData("PASSWORD: hunter2", "hunter2")]
    [InlineData("api_key=abc123def", "abc123def")]
    [InlineData("token: \"xyz-secret\"", "xyz-secret")]
    [InlineData("Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.payload", "eyJhbGciOiJIUzI1NiJ9")]
    [InlineData("connect mysql://user:dbpass123@mysql:3306/app", "dbpass123")]
    public void MaskSecrets_RemovesSecretValues(string input, string secret)
    {
        var masked = LogMasker.MaskSecrets(input);

        Assert.DoesNotContain(secret, masked);
        Assert.Contains("***", masked);
    }

    [Fact]
    public void MaskSecrets_KeepsNormalLogContent()
    {
        const string input = "2026-07-10 12:00:00 ERROR connection refused to 192.168.1.10:3306";

        var masked = LogMasker.MaskSecrets(input);

        Assert.Equal(input, masked);
    }
}

public class FailureSignatureTests
{
    [Fact]
    public void Compute_IsStableForSameFault()
    {
        var a = FailureSignature.Compute(1, "web", "ContainerStopped", "error at 2026-07-10 12:00:01 code 137");
        var b = FailureSignature.Compute(1, "web", "ContainerStopped", "error at 2026-07-11 08:30:59 code 137");

        // タイムスタンプ等の数値差は正規化され、同じ署名になる
        Assert.Equal(a, b);
    }

    [Fact]
    public void Compute_DiffersByTargetServiceClassification()
    {
        var baseline = FailureSignature.Compute(1, "web", "ContainerStopped", "err");

        Assert.NotEqual(baseline, FailureSignature.Compute(2, "web", "ContainerStopped", "err"));
        Assert.NotEqual(baseline, FailureSignature.Compute(1, "api", "ContainerStopped", "err"));
        Assert.NotEqual(baseline, FailureSignature.Compute(1, "web", "HttpUnavailable", "err"));
    }
}
