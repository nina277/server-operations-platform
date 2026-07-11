using System.Diagnostics;
using System.Text.Json;
using ServerOperations.Api.Adapters.Interfaces;

namespace ServerOperations.Api.Adapters.Implementations;

/// <summary>
/// Docker Engine APIアダプター。Socket ProxyまたはTLS保護済みAPIへHTTPで接続する。
/// docker.sockの直接マウントは使用しない。
/// </summary>
public class DockerAdapter(IHttpClientFactory httpClientFactory, ILogger<DockerAdapter> logger) : IDockerAdapter
{
    public const string HttpClientName = "docker-adapter";

    public async Task<AdapterConnectionResult> TestConnectionAsync(string endpoint, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var baseUri = new Uri(endpoint.TrimEnd('/') + "/");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client.GetAsync(new Uri(baseUri, "version"), ct);
            stopwatch.Stop();

            if (!response.IsSuccessStatusCode)
            {
                return new AdapterConnectionResult(
                    false,
                    $"Docker APIが異常応答を返しました(HTTP {(int)response.StatusCode})。",
                    stopwatch.ElapsedMilliseconds);
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var version = doc.RootElement.TryGetProperty("Version", out var v) ? v.GetString() : null;
            var apiVersion = doc.RootElement.TryGetProperty("ApiVersion", out var av) ? av.GetString() : null;

            return new AdapterConnectionResult(
                true,
                "Docker APIへ接続できました。",
                stopwatch.ElapsedMilliseconds,
                $"Engine {version ?? "unknown"} / API {apiVersion ?? "unknown"}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            // 例外メッセージに接続先詳細が含まれる可能性があるため、応答へはそのまま出さない
            logger.LogWarning(ex, "Docker connection test failed.");
            return new AdapterConnectionResult(
                false, "Docker APIへ接続できません(到達不能またはタイムアウト)。", stopwatch.ElapsedMilliseconds);
        }
    }
}
