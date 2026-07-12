using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;
using ServerOperations.Core.Adapters.Interfaces;

namespace ServerOperations.Core.Adapters.Implementations;

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
            try
            {
                using var doc = JsonDocument.Parse(json);
                var version = doc.RootElement.TryGetProperty("Version", out var v) ? v.GetString() : null;
                var apiVersion = doc.RootElement.TryGetProperty("ApiVersion", out var av) ? av.GetString() : null;

                return new AdapterConnectionResult(
                    true,
                    "Docker APIへ接続できました。",
                    stopwatch.ElapsedMilliseconds,
                    $"Engine {version ?? "unknown"} / API {apiVersion ?? "unknown"}");
            }
            catch (JsonException)
            {
                // リバースプロキシのエラーページ等、Docker API以外が応答したケース
                return new AdapterConnectionResult(
                    false,
                    "応答がDocker APIの形式ではありません。エンドポイントの設定を確認してください。",
                    stopwatch.ElapsedMilliseconds);
            }
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

    public async Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(
        string endpoint, string? composeProject = null, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var baseUri = new Uri(endpoint.TrimEnd('/') + "/");

        var path = "containers/json?all=true";
        if (!string.IsNullOrEmpty(composeProject))
        {
            var filters = JsonSerializer.Serialize(new Dictionary<string, string[]>
            {
                ["label"] = [$"com.docker.compose.project={composeProject}"],
            });
            path += $"&filters={Uri.EscapeDataString(filters)}";
        }

        using var response = await client.GetAsync(new Uri(baseUri, path), ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var result = new List<ContainerInfo>();
        foreach (var element in doc.RootElement.EnumerateArray())
        {
            var id = element.GetProperty("Id").GetString() ?? string.Empty;
            var name = element.TryGetProperty("Names", out var names) && names.GetArrayLength() > 0
                ? (names[0].GetString() ?? string.Empty).TrimStart('/')
                : id[..Math.Min(12, id.Length)];
            var image = element.TryGetProperty("Image", out var img) ? img.GetString() ?? string.Empty : string.Empty;
            var state = element.TryGetProperty("State", out var st) ? st.GetString() ?? string.Empty : string.Empty;
            var status = element.TryGetProperty("Status", out var stat) ? stat.GetString() ?? string.Empty : string.Empty;

            result.Add(new ContainerInfo(id, name, image, state, status, RestartCount: 0));
        }

        // 再起動回数はinspectでのみ取得できる(コンテナ数に上限を設けて追加照会する)
        for (var i = 0; i < result.Count && i < 30; i++)
        {
            result[i] = result[i] with { RestartCount = await GetRestartCountAsync(client, baseUri, result[i].Id, ct) };
        }

        return result;
    }

    public async Task<string> GetContainerLogsAsync(
        string endpoint, string containerId, int tailLines = 50, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var baseUri = new Uri(endpoint.TrimEnd('/') + "/");
        var path = $"containers/{Uri.EscapeDataString(containerId)}/logs?stdout=true&stderr=true&tail={Math.Clamp(tailLines, 1, 200)}";

        using var response = await client.GetAsync(new Uri(baseUri, path), ct);
        if (!response.IsSuccessStatusCode)
        {
            return string.Empty;
        }

        var raw = await response.Content.ReadAsByteArrayAsync(ct);
        return DecodeDockerLogStream(raw);
    }

    private static async Task<int> GetRestartCountAsync(
        HttpClient client, Uri baseUri, string containerId, CancellationToken ct)
    {
        try
        {
            using var response = await client.GetAsync(
                new Uri(baseUri, $"containers/{Uri.EscapeDataString(containerId)}/json"), ct);
            if (!response.IsSuccessStatusCode)
            {
                return 0;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            return doc.RootElement.TryGetProperty("RestartCount", out var rc) ? rc.GetInt32() : 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// Dockerログのmultiplexedストリーム(8バイトヘッダー付きフレーム)をテキストへ復号する。
    /// TTY有効コンテナはヘッダーなしのプレーンテキストが返るため、その場合はそのまま返す。
    /// </summary>
    internal static string DecodeDockerLogStream(byte[] raw)
    {
        if (raw.Length == 0)
        {
            return string.Empty;
        }

        // フレームヘッダー: [streamType(1), 0, 0, 0, length(4, big-endian)]
        var looksMultiplexed = raw.Length >= 8 && raw[0] is 0 or 1 or 2 && raw[1] == 0 && raw[2] == 0 && raw[3] == 0;
        if (!looksMultiplexed)
        {
            return System.Text.Encoding.UTF8.GetString(raw);
        }

        var builder = new System.Text.StringBuilder();
        var offset = 0;
        while (offset + 8 <= raw.Length)
        {
            var length = (raw[offset + 4] << 24) | (raw[offset + 5] << 16) | (raw[offset + 6] << 8) | raw[offset + 7];
            offset += 8;
            if (length <= 0 || offset + length > raw.Length)
            {
                break;
            }

            builder.Append(System.Text.Encoding.UTF8.GetString(raw, offset, length));
            offset += length;
        }

        return builder.ToString();
    }
}
