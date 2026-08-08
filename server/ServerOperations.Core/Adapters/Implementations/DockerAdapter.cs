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

    public async Task<ContainerStats?> GetContainerStatsAsync(
        string endpoint, string containerId, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var baseUri = new Uri(endpoint.TrimEnd('/') + "/");

        // one-shot=true は使わない。1周期しか取らないためprecpu_statsが0のまま返り、
        // CPU使用率を0との差分で計算することになる。
        // その値は「起動してからの平均」に近く、現在の使用率としては誤りである。
        // もっともらしい誤った数値はしきい値判定を狂わせるため、1秒待って正しい差分を取る。
        var path = $"containers/{Uri.EscapeDataString(containerId)}/stats?stream=false";

        try
        {
            using var response = await client.GetAsync(new Uri(baseUri, path), ct);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return ParseStats(await response.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Docker stats retrieval failed.");
            return null;
        }
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

    public async Task<AdapterConnectionResult> ControlContainerAsync(
        string endpoint, string containerNameOrId, ContainerOperation operation, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var baseUri = new Uri(endpoint.TrimEnd('/') + "/");
        var verb = operation switch
        {
            ContainerOperation.Start => "start",
            ContainerOperation.Stop => "stop",
            ContainerOperation.Restart => "restart",
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };

        var path = $"containers/{Uri.EscapeDataString(containerNameOrId)}/{verb}";
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var response = await client.PostAsync(new Uri(baseUri, path), content: null, ct);
            stopwatch.Stop();

            // 304 = 既に目的の状態(開始済み/停止済み)。冪等な操作として成功扱いにする
            if (response.IsSuccessStatusCode || (int)response.StatusCode == 304)
            {
                var note = (int)response.StatusCode == 304 ? "(既に目的の状態でした)" : string.Empty;
                return new AdapterConnectionResult(
                    true, $"コンテナ {containerNameOrId} の{verb}に成功しました。{note}", stopwatch.ElapsedMilliseconds);
            }

            return new AdapterConnectionResult(
                false,
                $"コンテナ {containerNameOrId} の{verb}に失敗しました(HTTP {(int)response.StatusCode})。",
                stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "Docker container control failed. operation={Operation}", verb);
            return new AdapterConnectionResult(
                false, "Docker APIへ接続できません(到達不能またはタイムアウト)。", stopwatch.ElapsedMilliseconds);
        }
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
    /// Docker statsの応答からCPU・メモリ使用率を取り出す。
    /// 算出に必要な値が揃わない項目はnullを返す(0で埋めない)。
    /// </summary>
    internal static ContainerStats? ParseStats(string json)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var cpuPercent = ComputeCpuPercent(root);
            var (memoryPercent, memoryUsed, memoryLimit) = ComputeMemory(root);

            if (cpuPercent is null && memoryPercent is null && memoryUsed is null)
            {
                // 停止直後のコンテナなどでは空の統計が返る。何も取れないなら未取得として扱う
                return null;
            }

            return new ContainerStats(cpuPercent, memoryPercent, memoryUsed, memoryLimit);
        }
    }

    /// <summary>
    /// CPU使用率 = (今回と前回のCPU時間の差 / 同じ区間のシステム全体のCPU時間の差) × コア数 × 100。
    /// Dockerは累積値しか返さないため、差分が取れなければ算出しない。
    /// </summary>
    private static double? ComputeCpuPercent(JsonElement root)
    {
        if (!root.TryGetProperty("cpu_stats", out var cpu) ||
            !root.TryGetProperty("precpu_stats", out var precpu))
        {
            return null;
        }

        var total = ReadInt64(cpu, "cpu_usage", "total_usage");
        var preTotal = ReadInt64(precpu, "cpu_usage", "total_usage");
        var system = ReadInt64(cpu, "system_cpu_usage");
        var preSystem = ReadInt64(precpu, "system_cpu_usage");

        if (total is null || preTotal is null || system is null || preSystem is null)
        {
            return null;
        }

        // 前回のシステムCPU時間は起動からの累積であり、実際に前周期を測っていれば必ず正になる。
        // 0のまま返るのは前周期が無い場合(one-shot取得)であり、
        // このとき差分は「コンテナが起動してから今まで」を指す。
        // 計算自体は成立してしまうが、現在の使用率としては誤りなので算出しない。
        if (preSystem.Value <= 0)
        {
            return null;
        }

        var cpuDelta = total.Value - preTotal.Value;
        var systemDelta = system.Value - preSystem.Value;

        // 差分が負や0になるのは、前回値が無い(one-shot)かカウンタが巻き戻った場合。
        // どちらも意味のある使用率にならないため算出しない
        if (cpuDelta < 0 || systemDelta <= 0)
        {
            return null;
        }

        var onlineCpus = ReadInt64(cpu, "online_cpus")
            ?? (cpu.TryGetProperty("cpu_usage", out var usage) &&
                usage.TryGetProperty("percpu_usage", out var perCpu) &&
                perCpu.ValueKind == JsonValueKind.Array
                    ? perCpu.GetArrayLength()
                    : 0);
        if (onlineCpus <= 0)
        {
            return null;
        }

        // 全コアを使い切っても100%になるよう、コア数を掛けたうえで正規化する
        return Math.Round((double)cpuDelta / systemDelta * onlineCpus * 100.0, 2);
    }

    /// <summary>
    /// メモリ使用率を求める。ページキャッシュ(inactive_file)は使用量から差し引く。
    /// 差し引かないと、ファイルを読み書きしただけのコンテナが常に上限近くに見え、
    /// しきい値ルールが誤って発火する。
    /// </summary>
    private static (double? Percent, long? Used, long? Limit) ComputeMemory(JsonElement root)
    {
        if (!root.TryGetProperty("memory_stats", out var memory))
        {
            return (null, null, null);
        }

        var usage = ReadInt64(memory, "usage");
        if (usage is null)
        {
            return (null, null, null);
        }

        long cache = 0;
        if (memory.TryGetProperty("stats", out var stats) && stats.ValueKind == JsonValueKind.Object)
        {
            // cgroup v2 は inactive_file、v1 は total_inactive_file を使う
            cache = ReadInt64(stats, "inactive_file")
                ?? ReadInt64(stats, "total_inactive_file")
                ?? ReadInt64(stats, "cache")
                ?? 0;
        }

        var used = Math.Max(0, usage.Value - cache);
        var limit = ReadInt64(memory, "limit");

        // 上限が無い(または0)場合は割合を出せない。使用量だけを返す
        if (limit is null || limit.Value <= 0)
        {
            return (null, used, null);
        }

        return (Math.Round((double)used / limit.Value * 100.0, 2), used, limit.Value);
    }

    private static long? ReadInt64(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(property, out var value) ||
            value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt64(out var parsed) ? parsed : null;
    }

    private static long? ReadInt64(JsonElement parent, string property, string nestedProperty)
    {
        if (parent.ValueKind != JsonValueKind.Object ||
            !parent.TryGetProperty(property, out var nested))
        {
            return null;
        }

        return ReadInt64(nested, nestedProperty);
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
