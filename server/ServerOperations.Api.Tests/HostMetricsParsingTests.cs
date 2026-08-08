using ServerOperations.Core.Adapters.Implementations;

namespace ServerOperations.Api.Tests;

/// <summary>
/// node_exporter のテキスト形式からファイルシステム使用率を取り出す部分。
///
/// 使用率の計算がdfとずれると、しきい値の意味が利用者の感覚から離れる。
/// 読めなかったものを0%にしないことと合わせて確かめる。
/// </summary>
public class HostMetricsParsingTests
{
    private const string RootFilesystem = """
        # HELP node_filesystem_size_bytes Filesystem size in bytes.
        # TYPE node_filesystem_size_bytes gauge
        node_filesystem_size_bytes{device="/dev/sda1",fstype="ext4",mountpoint="/"} 1000
        node_filesystem_free_bytes{device="/dev/sda1",fstype="ext4",mountpoint="/"} 200
        node_filesystem_avail_bytes{device="/dev/sda1",fstype="ext4",mountpoint="/"} 100
        """;

    [Fact]
    public void 使用率はdfと同じ計算にする()
    {
        // 全容量1000、空き200、うち一般利用者が使えるのは100。
        // 使用量は 1000-200=800 なので、dfのUse%は 800/(800+100)=88.89%。
        // 全容量を分母にすると80%になり、**実際には書けないのに余裕があるように見える**
        var result = HostMetricsAdapter.ParseFilesystemUsage(RootFilesystem);

        var fs = Assert.Single(result);
        Assert.Equal("/", fs.Mountpoint);
        Assert.Equal(88.89, fs.UsagePercent);
        Assert.Equal(1000, fs.SizeBytes);
        Assert.Equal(100, fs.AvailableBytes);
    }

    [Fact]
    public void 空き容量が無ければ利用可能量から求める()
    {
        var result = HostMetricsAdapter.ParseFilesystemUsage("""
            node_filesystem_size_bytes{fstype="xfs",mountpoint="/data"} 1000
            node_filesystem_avail_bytes{fstype="xfs",mountpoint="/data"} 250
            """);

        var fs = Assert.Single(result);
        Assert.Equal(75.0, fs.UsagePercent);
    }

    [Fact]
    public void 実体の無いファイルシステムは除く()
    {
        // tmpfsやoverlayは実ディスクの残量を表さない。
        // 混ぜると「ディスク使用率」の意味が変わってしまう
        var result = HostMetricsAdapter.ParseFilesystemUsage($$"""
            {{RootFilesystem}}
            node_filesystem_size_bytes{fstype="tmpfs",mountpoint="/run"} 100
            node_filesystem_avail_bytes{fstype="tmpfs",mountpoint="/run"} 1
            node_filesystem_size_bytes{fstype="overlay",mountpoint="/var/lib/docker/overlay2/x"} 100
            node_filesystem_avail_bytes{fstype="overlay",mountpoint="/var/lib/docker/overlay2/x"} 1
            node_filesystem_size_bytes{fstype="fuse.portal",mountpoint="/run/user/1000/doc"} 100
            node_filesystem_avail_bytes{fstype="fuse.portal",mountpoint="/run/user/1000/doc"} 1
            """);

        var fs = Assert.Single(result);
        Assert.Equal("/", fs.Mountpoint);
    }

    [Fact]
    public void 指数表記の値を読める()
    {
        // node_exporterは大きな値を 5e+10 の形で出す
        var result = HostMetricsAdapter.ParseFilesystemUsage("""
            node_filesystem_size_bytes{fstype="ext4",mountpoint="/"} 1e+02
            node_filesystem_avail_bytes{fstype="ext4",mountpoint="/"} 2.5e+01
            """);

        var fs = Assert.Single(result);
        Assert.Equal(100, fs.SizeBytes);
        Assert.Equal(75.0, fs.UsagePercent);
    }

    [Fact]
    public void 値の後ろに時刻が付いていても読める()
    {
        var result = HostMetricsAdapter.ParseFilesystemUsage("""
            node_filesystem_size_bytes{fstype="ext4",mountpoint="/"} 1000 1750000000000
            node_filesystem_avail_bytes{fstype="ext4",mountpoint="/"} 250 1750000000000
            """);

        Assert.Single(result);
    }

    [Fact]
    public void 片方しか無いファイルシステムは結果に含めない()
    {
        // 容量だけ分かっても使用率は出せない。0%として出すと空きがあるように見える
        var result = HostMetricsAdapter.ParseFilesystemUsage("""
            node_filesystem_size_bytes{fstype="ext4",mountpoint="/"} 1000
            """);

        Assert.Empty(result);
    }

    [Fact]
    public void 容量が0のものは含めない()
    {
        var result = HostMetricsAdapter.ParseFilesystemUsage("""
            node_filesystem_size_bytes{fstype="ext4",mountpoint="/snap"} 0
            node_filesystem_avail_bytes{fstype="ext4",mountpoint="/snap"} 0
            """);

        Assert.Empty(result);
    }

    [Fact]
    public void NaNや無限大は読み飛ばす()
    {
        // 取得に失敗した項目でnode_exporterはNaNを返すことがある
        var result = HostMetricsAdapter.ParseFilesystemUsage("""
            node_filesystem_size_bytes{fstype="ext4",mountpoint="/"} NaN
            node_filesystem_avail_bytes{fstype="ext4",mountpoint="/"} +Inf
            """);

        Assert.Empty(result);
    }

    [Fact]
    public void 関係の無い行は無視する()
    {
        var result = HostMetricsAdapter.ParseFilesystemUsage($$"""
            node_cpu_seconds_total{cpu="0",mode="idle"} 12345.6
            node_memory_MemAvailable_bytes 987654321

            {{RootFilesystem}}
            """);

        Assert.Single(result);
    }

    [Fact]
    public void 形式が違っても例外にしない()
    {
        Assert.Empty(HostMetricsAdapter.ParseFilesystemUsage("<html>404 Not Found</html>"));
        Assert.Empty(HostMetricsAdapter.ParseFilesystemUsage(string.Empty));
    }

    [Fact]
    public void 複数のファイルシステムをマウントポイント順に返す()
    {
        var result = HostMetricsAdapter.ParseFilesystemUsage("""
            node_filesystem_size_bytes{fstype="ext4",mountpoint="/mnt/data"} 1000
            node_filesystem_avail_bytes{fstype="ext4",mountpoint="/mnt/data"} 500
            node_filesystem_size_bytes{fstype="ext4",mountpoint="/"} 1000
            node_filesystem_avail_bytes{fstype="ext4",mountpoint="/"} 100
            """);

        Assert.Equal(["/", "/mnt/data"], result.Select(f => f.Mountpoint));
    }

    [Fact]
    public void 扱うファイルシステムの数に上限がある()
    {
        var lines = Enumerable.Range(1, HostMetricsAdapter.MaxFilesystems + 10)
            .SelectMany(i => new[]
            {
                $$"""node_filesystem_size_bytes{fstype="ext4",mountpoint="/m{{i:000}}"} 1000""",
                $$"""node_filesystem_avail_bytes{fstype="ext4",mountpoint="/m{{i:000}}"} 500""",
            });

        var result = HostMetricsAdapter.ParseFilesystemUsage(string.Join('\n', lines));

        Assert.Equal(HostMetricsAdapter.MaxFilesystems, result.Count);
    }
}
