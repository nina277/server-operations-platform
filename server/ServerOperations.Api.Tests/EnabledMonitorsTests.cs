using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Tests;

/// <summary>
/// 対象ごとに行う収集の種類。
///
/// 「どれができるか」はテンプレートが決め、「今回どれを使うか」は対象が決める。
/// 未設定はテンプレートの既定を意味し、「何もしない」とは区別する。
/// </summary>
public class EnabledMonitorsTests
{
    private static readonly AdapterTemplateCatalog Catalog = new();

    private static readonly AdapterTemplate DockerTemplate =
        Catalog.Find(AdapterTemplateCatalog.DockerHost)!;

    private static readonly AdapterTemplate WebTemplate =
        Catalog.Find(AdapterTemplateCatalog.WebSite)!;

    private static MonitoringTarget Target(string? json = null) => new()
    {
        Id = 1,
        Name = "t1",
        TemplateId = AdapterTemplateCatalog.DockerHost,
        EnabledMonitorsJson = json,
    };

    // --- 既定 ---

    [Fact]
    public void 未設定ならテンプレートで行えるものすべて()
    {
        var resolved = EnabledMonitors.Resolve(Target(), DockerTemplate);

        Assert.Equal(DockerTemplate.CollectableMonitors, resolved);
    }

    [Fact]
    public void 空配列は何もしないではなく既定として扱う()
    {
        // 「全部外す」なら監視自体を止めるべき。
        // 収集だけ止めた対象は「監視しているのに何も見ていない」状態になる。
        var resolved = EnabledMonitors.Resolve(Target("[]"), DockerTemplate);

        Assert.Equal(DockerTemplate.CollectableMonitors, resolved);
    }

    [Fact]
    public void 壊れた設定は既定として扱う()
    {
        // 空として扱うと、設定が壊れただけで監視が黙って止まる
        var resolved = EnabledMonitors.Resolve(Target("これはJSONではない"), DockerTemplate);

        Assert.Equal(DockerTemplate.CollectableMonitors, resolved);
    }

    // --- 選択 ---

    [Fact]
    public void 選んだものだけを返す()
    {
        var resolved = EnabledMonitors.Resolve(
            Target($"[\"{MonitorKinds.ContainerState}\"]"), DockerTemplate);

        Assert.Equal([MonitorKinds.ContainerState], resolved);
    }

    [Fact]
    public void テンプレートで行えない種類は無視する()
    {
        // 保存時に弾いているが、後からテンプレートが変わることもある
        var resolved = EnabledMonitors.Resolve(
            Target($"[\"{MonitorKinds.ContainerState}\",\"{MonitorKinds.HttpCheck}\"]"),
            DockerTemplate);

        Assert.Equal([MonitorKinds.ContainerState], resolved);
    }

    [Fact]
    public void 有効かどうかを判定できる()
    {
        var target = Target($"[\"{MonitorKinds.ContainerState}\"]");

        Assert.True(EnabledMonitors.IsEnabled(target, DockerTemplate, MonitorKinds.ContainerState));
        Assert.False(EnabledMonitors.IsEnabled(target, DockerTemplate, MonitorKinds.LogExcerpt));
    }

    // --- 保存する値 ---

    [Fact]
    public void 未指定はnullとして保存する()
    {
        Assert.Null(EnabledMonitors.Serialize(null, DockerTemplate));
    }

    [Fact]
    public void すべて選んだ場合もnullとして保存する()
    {
        // 「既定のまま」と同じ状態にしておくと、後からテンプレートに
        // 収集が増えたときに自動で追従できる
        var json = EnabledMonitors.Serialize(DockerTemplate.CollectableMonitors, DockerTemplate);

        Assert.Null(json);
    }

    [Fact]
    public void 一部だけ選んだ場合は保存する()
    {
        var json = EnabledMonitors.Serialize([MonitorKinds.ContainerState], DockerTemplate);

        Assert.NotNull(json);
        Assert.Contains(MonitorKinds.ContainerState, json);
    }

    [Fact]
    public void 空の選択はnullとして保存する()
    {
        Assert.Null(EnabledMonitors.Serialize([], DockerTemplate));
    }

    [Fact]
    public void 重複と空白を落として保存する()
    {
        var json = EnabledMonitors.Serialize(
            [MonitorKinds.ContainerState, $" {MonitorKinds.ContainerState} ", "  "], DockerTemplate);

        Assert.Equal($"[\"{MonitorKinds.ContainerState}\"]", json);
    }

    // --- 検証 ---

    [Fact]
    public void テンプレートで行えない種類は保存前に拒否する()
    {
        // 黙って捨てると、選んだのに効かない設定ができる
        var ex = Assert.Throws<AppException>(() =>
            EnabledMonitors.Validate([MonitorKinds.HttpCheck], DockerTemplate));

        Assert.Equal("unknown_monitor", ex.Code);
    }

    [Fact]
    public void 知らない名前も拒否する()
    {
        var ex = Assert.Throws<AppException>(() =>
            EnabledMonitors.Validate(["cpu"], DockerTemplate));

        Assert.Equal("unknown_monitor", ex.Code);
    }

    [Fact]
    public void 行える種類なら通す()
    {
        EnabledMonitors.Validate([MonitorKinds.ContainerState], DockerTemplate);
        EnabledMonitors.Validate([MonitorKinds.HttpCheck], WebTemplate);
    }

    // --- テンプレートの定義 ---

    [Fact]
    public void 選べる種類は既知のものだけ()
    {
        foreach (var template in Catalog.GetAll())
        {
            Assert.All(template.CollectableMonitors, m => Assert.Contains(m, MonitorKinds.All));
        }
    }

    [Fact]
    public void すべてのテンプレートに選べる種類がある()
    {
        // 空だと、そのテンプレートの対象は何も収集しないことになる
        Assert.All(Catalog.GetAll(), t => Assert.NotEmpty(t.CollectableMonitors));
    }

    /// <summary>
    /// 案内に載せる監視項目と、それを実際に取りに行く収集の対応。
    /// ここに無い項目を案内へ足すと下の試験が落ちる。
    /// </summary>
    private static readonly Dictionary<string, string> BackedBy = new()
    {
        ["container-state"] = MonitorKinds.ContainerState,
        ["restart-count"] = MonitorKinds.ContainerState,
        ["log-excerpt"] = MonitorKinds.LogExcerpt,
        ["cpu"] = MonitorKinds.ResourceUsage,
        ["memory"] = MonitorKinds.ResourceUsage,
        ["disk"] = MonitorKinds.DiskUsage,
        ["http-status"] = MonitorKinds.HttpCheck,
        ["http-latency"] = MonitorKinds.HttpCheck,
    };

    [Fact]
    public void 案内する監視項目には収集の手段がある()
    {
        // 案内に載せて収集できないと「設定したのに値が出ない」ことになる。
        // 過去に cpu / memory / disk がこの状態で並んでいた。
        foreach (var template in Catalog.GetAll())
        {
            foreach (var monitor in template.RecommendedMonitors)
            {
                Assert.True(
                    BackedBy.TryGetValue(monitor, out var kind),
                    $"案内にある {monitor} を取りに行く収集がありません。");
                Assert.Contains(kind!, template.CollectableMonitors);
            }
        }
    }
}
