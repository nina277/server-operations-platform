using ServerOperations.Core.Adapters.Implementations;

namespace ServerOperations.Api.Tests;

/// <summary>
/// Docker statsの応答からCPU・メモリ使用率を取り出す部分。
///
/// ここで誤った値を作ると、しきい値ルールが誤って発火する(あるいは逼迫を見逃す)。
/// 算出できないものはnullにする、という一点を主に確かめる。
/// </summary>
public class DockerStatsParsingTests
{
    /// <summary>
    /// Docker statsの応答を組み立てる。実際の応答はもっと項目が多いが、
    /// 算出に使う値だけを持つ形でも同じ結果になる必要がある。
    /// </summary>
    private static string StatsJson(
        long totalUsage,
        long preTotalUsage,
        long systemUsage,
        long preSystemUsage,
        int onlineCpus,
        long memoryUsage,
        long memoryLimit,
        long inactiveFile = 0) =>
        $$"""
        {
          "cpu_stats": {
            "cpu_usage": { "total_usage": {{totalUsage}} },
            "system_cpu_usage": {{systemUsage}},
            "online_cpus": {{onlineCpus}}
          },
          "precpu_stats": {
            "cpu_usage": { "total_usage": {{preTotalUsage}} },
            "system_cpu_usage": {{preSystemUsage}}
          },
          "memory_stats": {
            "usage": {{memoryUsage}},
            "limit": {{memoryLimit}},
            "stats": { "inactive_file": {{inactiveFile}} }
          }
        }
        """;

    [Fact]
    public void CPU使用率は前回との差分から求める()
    {
        // コンテナが1億ns、システム全体が4億ns進み、コアが4つ。
        // 1コア分を使い切った状態なので 100%
        var stats = DockerAdapter.ParseStats(StatsJson(
            totalUsage: 200_000_000, preTotalUsage: 100_000_000,
            systemUsage: 800_000_000, preSystemUsage: 400_000_000,
            onlineCpus: 4, memoryUsage: 100, memoryLimit: 1000));

        Assert.NotNull(stats);
        Assert.Equal(100.0, stats.CpuUsagePercent);
    }

    [Fact]
    public void 前回値が無ければCPU使用率を出さない()
    {
        // one-shot取得ではprecpu_statsが0のまま返る。
        // そのまま計算すると「起動してからの平均」に近い値になり、
        // 現在の使用率としては誤りになる。0%と答えるのも同じく誤り。
        var stats = DockerAdapter.ParseStats(StatsJson(
            totalUsage: 200_000_000, preTotalUsage: 0,
            systemUsage: 800_000_000, preSystemUsage: 0,
            onlineCpus: 4, memoryUsage: 100, memoryLimit: 1000));

        Assert.NotNull(stats);
        Assert.Null(stats.CpuUsagePercent);
    }

    [Fact]
    public void メモリ使用率はページキャッシュを差し引く()
    {
        // 差し引かないと、ファイルを読み書きしただけのコンテナが
        // 常に上限近くに見え、しきい値ルールが誤って発火する
        var stats = DockerAdapter.ParseStats(StatsJson(
            totalUsage: 2, preTotalUsage: 1, systemUsage: 200, preSystemUsage: 100, onlineCpus: 1,
            memoryUsage: 900, memoryLimit: 1000, inactiveFile: 400));

        Assert.NotNull(stats);
        Assert.Equal(50.0, stats.MemoryUsagePercent);
        Assert.Equal(500, stats.MemoryUsageBytes);
    }

    [Fact]
    public void cgroupV1の項目名でも差し引く()
    {
        var stats = DockerAdapter.ParseStats("""
        {
          "memory_stats": {
            "usage": 900,
            "limit": 1000,
            "stats": { "total_inactive_file": 400 }
          }
        }
        """);

        Assert.NotNull(stats);
        Assert.Equal(50.0, stats.MemoryUsagePercent);
    }

    [Fact]
    public void メモリ上限が無ければ割合を出さない()
    {
        var stats = DockerAdapter.ParseStats("""
        { "memory_stats": { "usage": 900, "limit": 0 } }
        """);

        Assert.NotNull(stats);
        Assert.Null(stats.MemoryUsagePercent);
        Assert.Equal(900, stats.MemoryUsageBytes);
    }

    [Fact]
    public void 統計が空なら未取得として扱う()
    {
        // 停止直後のコンテナでは中身の無い統計が返る。
        // 0%として記録すると「使っていない」という正常値になってしまう
        Assert.Null(DockerAdapter.ParseStats("""{ "cpu_stats": {}, "memory_stats": {} }"""));
    }

    [Fact]
    public void 応答が壊れていても例外にしない()
    {
        Assert.Null(DockerAdapter.ParseStats("これはJSONではない"));
        Assert.Null(DockerAdapter.ParseStats("[]"));
    }

    [Fact]
    public void コア数が無ければpercpu_usageの数で代用する()
    {
        var stats = DockerAdapter.ParseStats("""
        {
          "cpu_stats": {
            "cpu_usage": { "total_usage": 200, "percpu_usage": [50, 50, 50, 50] },
            "system_cpu_usage": 800
          },
          "precpu_stats": {
            "cpu_usage": { "total_usage": 100 },
            "system_cpu_usage": 400
          }
        }
        """);

        Assert.NotNull(stats);
        Assert.Equal(100.0, stats.CpuUsagePercent);
    }
}
