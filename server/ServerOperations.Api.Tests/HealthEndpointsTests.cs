using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ServerOperations.Api.Tests;

/// <summary>
/// live と ready の役割の違いを確かめる。
///
/// live はプロセスが応答できるかだけを見る。DBが落ちただけでプロセスを
/// 再起動させても直らないため、依存を混ぜない。
/// ready は依存を含めて「受付できるか」を見る。
///
/// この試験ではDBを用意していないため、ready は落ちるのが正しい。
/// </summary>
public class HealthEndpointsTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task liveness_はDBが無くても200を返す()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task readiness_はDBへ接続できなければ503を返す()
    {
        // 手順書はreadinessで「DB接続まで確認」と書いている。
        // ここが200を返すなら、その記述と実装が食い違っている。
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task readiness_の応答に接続先の情報を含めない()
    {
        // 未認証で叩ける口のため、失敗時に接続文字列やホスト名を出さない
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
    }
}
