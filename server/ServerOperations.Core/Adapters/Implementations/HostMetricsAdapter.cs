using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Adapters.Interfaces;

namespace ServerOperations.Core.Adapters.Implementations;

/// <summary>
/// ホストのメトリクス取得アダプター。node_exporter が公開するテキスト形式を読む。
///
/// **GETして読むだけで、ホストに対して何も実行しない。**
/// 接続先は登録済みの監視対象に紐づくURLに限り、保存時と接続時の両方で検証する
/// (接続時の検証は共通のガード付きハンドラーが行う)。
/// </summary>
public class HostMetricsAdapter(
    IHttpClientFactory httpClientFactory,
    ILogger<HostMetricsAdapter> logger) : IHostMetricsAdapter
{
    public const string HttpClientName = "host-metrics-adapter";

    /// <summary>
    /// 読み込む応答の上限。node_exporterの出力は通常数百KBに収まる。
    /// 上限を設けないと、際限なく流し続ける相手にメモリを埋められる。
    /// </summary>
    internal const int MaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>扱うファイルシステムの数の上限。</summary>
    internal const int MaxFilesystems = 50;

    /// <summary>
    /// 実体のあるファイルシステムだけを見る。
    /// tmpfsやoverlayは実ディスクの残量を表さないため、混ぜると使用率の意味が変わる。
    /// </summary>
    private static readonly string[] PseudoFilesystemTypes =
        ["tmpfs", "devtmpfs", "overlay", "squashfs", "ramfs", "rootfs", "autofs", "nsfs"];

    private const string SizeMetric = "node_filesystem_size_bytes";
    private const string AvailableMetric = "node_filesystem_avail_bytes";
    private const string FreeMetric = "node_filesystem_free_bytes";

    /// <summary>
    /// ラベルの取り出し。値の中に引用符を含む形は扱わない。
    /// マウントポイントやファイルシステム種別に引用符は現れず、
    /// 単純な形に限ることで後戻りの起きない照合になる。
    /// </summary>
    private static readonly Regex LabelPattern = new(
        """
        (\w+)="([^"]*)"
        """,
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(200));

    public async Task<IReadOnlyList<FilesystemUsage>> GetFilesystemUsageAsync(
        string metricsUrl, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using var response = await client.GetAsync(
                metricsUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Host metrics endpoint returned HTTP {Status}.", (int)response.StatusCode);
                return [];
            }

            var text = await ReadBoundedAsync(response.Content, ct);
            return ParseFilesystemUsage(text);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // 接続先の詳細が例外に含まれうるため、そのままログへ出さず要点だけ残す
            logger.LogWarning(ex, "Host metrics retrieval failed.");
            return [];
        }
    }

    /// <summary>応答を上限付きで読む。上限に達したらそこで打ち切る。</summary>
    private static async Task<string> ReadBoundedAsync(HttpContent content, CancellationToken ct)
    {
        await using var stream = await content.ReadAsStreamAsync(ct);
        var buffer = new byte[81920];
        using var memory = new MemoryStream();

        while (memory.Length < MaxResponseBytes)
        {
            var read = await stream.ReadAsync(buffer, ct);
            if (read == 0)
            {
                break;
            }

            memory.Write(buffer, 0, (int)Math.Min(read, MaxResponseBytes - memory.Length));
        }

        return Encoding.UTF8.GetString(memory.ToArray());
    }

    /// <summary>
    /// Prometheusのテキスト形式からファイルシステムごとの使用状況を取り出す。
    /// 必要な値が揃わないファイルシステムは結果に含めない(0%として扱わない)。
    /// </summary>
    internal static IReadOnlyList<FilesystemUsage> ParseFilesystemUsage(string text)
    {
        var sizes = new Dictionary<string, double>(StringComparer.Ordinal);
        var available = new Dictionary<string, double>(StringComparer.Ordinal);
        var free = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var target = line.StartsWith(SizeMetric, StringComparison.Ordinal) ? sizes
                : line.StartsWith(AvailableMetric, StringComparison.Ordinal) ? available
                : line.StartsWith(FreeMetric, StringComparison.Ordinal) ? free
                : null;
            if (target is null)
            {
                continue;
            }

            if (!TryParseSample(line, out var mountpoint, out var fstype, out var value))
            {
                continue;
            }

            if (PseudoFilesystemTypes.Contains(fstype, StringComparer.OrdinalIgnoreCase) ||
                fstype.StartsWith("fuse.", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            target[mountpoint] = value;
        }

        var result = new List<FilesystemUsage>();

        foreach (var (mountpoint, size) in sizes.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            // 容量が0のものは割合を出せない
            if (size <= 0 || !available.TryGetValue(mountpoint, out var avail) || avail < 0)
            {
                continue;
            }

            // dfと同じ計算にする。
            // ext4などはroot専用の予備領域を持つため、全容量を分母にすると
            // 一般利用者が書けなくなっても「まだ空きがある」と表示されてしまう。
            // 実際に書ける量(avail)を基準にすることで、dfのUse%と一致する。
            var used = free.TryGetValue(mountpoint, out var freeBytes) && freeBytes >= 0
                ? size - freeBytes
                : size - avail;
            if (used < 0)
            {
                continue;
            }

            var denominator = used + avail;
            if (denominator <= 0)
            {
                continue;
            }

            result.Add(new FilesystemUsage(
                mountpoint,
                (long)size,
                (long)avail,
                Math.Round(used / denominator * 100.0, 2)));

            if (result.Count >= MaxFilesystems)
            {
                break;
            }
        }

        return result;
    }

    private static bool TryParseSample(
        string line, out string mountpoint, out string fstype, out double value)
    {
        mountpoint = string.Empty;
        fstype = string.Empty;
        value = 0;

        var open = line.IndexOf('{');
        var close = line.LastIndexOf('}');
        if (open < 0 || close < open)
        {
            return false;
        }

        // 値の後ろに時刻が付くこともあるため、最初の要素だけを見る
        var tail = line[(close + 1)..].Trim();
        var valueText = tail.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (valueText is null ||
            !double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
            double.IsNaN(value) || double.IsInfinity(value))
        {
            return false;
        }

        try
        {
            foreach (Match match in LabelPattern.Matches(line[(open + 1)..close]))
            {
                switch (match.Groups[1].Value)
                {
                    case "mountpoint":
                        mountpoint = match.Groups[2].Value;
                        break;
                    case "fstype":
                        fstype = match.Groups[2].Value;
                        break;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }

        return mountpoint.Length > 0;
    }
}
