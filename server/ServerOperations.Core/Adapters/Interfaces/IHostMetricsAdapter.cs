namespace ServerOperations.Core.Adapters.Interfaces;

/// <summary>
/// ホストのファイルシステム1つ分の使用状況。
///
/// 使用率はdfと同じ計算にする。全容量に対する割合ではなく、
/// **一般利用者が実際に書ける量**に対する割合で出す(下のUsagePercentを参照)。
/// </summary>
public record FilesystemUsage(
    string Mountpoint,
    long SizeBytes,
    long AvailableBytes,
    double UsagePercent);

/// <summary>
/// ホストのメトリクス取得。node_exporter のような読み取り専用のエンドポイントを想定する。
///
/// **取得するだけで、ホストに対して何も実行しない。**
/// 監視対象のサーバー上でコマンドを走らせる仕組みは持たない。
/// </summary>
public interface IHostMetricsAdapter
{
    /// <summary>
    /// ファイルシステムごとの使用状況を取得する。
    /// 到達できない、または形式が読めない場合は空を返す(0%として扱わない)。
    /// </summary>
    Task<IReadOnlyList<FilesystemUsage>> GetFilesystemUsageAsync(
        string metricsUrl, CancellationToken ct = default);
}
