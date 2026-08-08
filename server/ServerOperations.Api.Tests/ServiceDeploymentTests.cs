using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServerOperations.Api.Tests.Fakes;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services.Deployment;

namespace ServerOperations.Api.Tests;

/// <summary>
/// テンプレートからのサービス展開。
///
/// 展開は**第2層(人が明示的に起動する運用操作)**であり、
/// 診断・AI・ルールからは到達できない(ActionTierBoundaryTests が保証する)。
///
/// ここで固定するのは「テンプレートで表現できないこと」。
/// Composeを丸ごと受け取らず、1コンテナ + 環境変数 + ポート + 名前付きボリュームに
/// 限っているため、**特権やホストのマウントはそもそも指定できない。**
/// </summary>
public class ServiceDeploymentTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeDeploymentAdapter _adapter = new();
    private readonly AppDbContext _db = new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"deploy-{Guid.NewGuid()}")
            .Options);

    private ServiceDeploymentService CreateSut() => new(
        _db, _adapter, new TestTimeProvider(Now),
        NullLogger<ServiceDeploymentService>.Instance);

    private async Task<(long TargetId, long TemplateId)> SeedAsync(
        string image = "nginx:1.27-alpine", bool withDeployEndpoint = true)
    {
        var settings = new Dictionary<string, string> { ["endpoint"] = "http://socket-proxy:2375" };
        if (withDeployEndpoint)
        {
            settings["deployEndpoint"] = "http://deploy-proxy:2375";
        }

        var target = new MonitoringTarget
        {
            Name = "docker1", TemplateId = "docker-host",
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            Profile = new TargetProfile { SettingsJson = JsonSerializer.Serialize(settings) },
        };
        var template = new ServiceTemplate
        {
            Key = "web", Name = "Web", Image = image, MemoryLimitMb = 128,
            CreatedAt = Now.UtcDateTime, UpdatedAt = Now.UtcDateTime,
            Inputs =
            [
                new ServiceTemplateInput
                {
                    Key = "HTTP_PORT", Label = "公開ポート", Type = ServiceInputType.Port,
                    ContainerPort = 80, DefaultValue = "8081", SortOrder = 0,
                },
                new ServiceTemplateInput
                {
                    Key = "html", Label = "配信内容", Type = ServiceInputType.Volume,
                    ContainerPath = "/usr/share/nginx/html", DefaultValue = "html", SortOrder = 1,
                },
                new ServiceTemplateInput
                {
                    Key = "ADMIN_TOKEN", Label = "管理トークン", Type = ServiceInputType.Secret,
                    Required = false, SortOrder = 2,
                },
            ],
        };
        _db.MonitoringTargets.Add(target);
        _db.ServiceTemplates.Add(template);
        await _db.SaveChangesAsync();
        return (target.Id, template.Id);
    }

    // --- 展開できない指定 -----------------------------------------------

    [Fact]
    public async Task 版を指定しないイメージのテンプレートは展開できない()
    {
        // 同じテンプレートから展開しても、時期によって別のものが動く
        var (targetId, templateId) = await SeedAsync(image: "nginx:latest");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().PreviewAsync(targetId, templateId, "web", []));

        Assert.Contains("latest", error.Message);
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("Web")]
    [InlineData("a")]
    [InlineData("web;rm -rf /")]
    [InlineData("web name")]
    public async Task 不正なサービス名は受け付けない(string name)
    {
        var (targetId, templateId) = await SeedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().PreviewAsync(targetId, templateId, name, []));
    }

    [Theory]
    [InlineData("8080")]
    [InlineData("3306")]
    public async Task 予約ポートは使えない(string port)
    {
        // SSHや監視システム自身のポートを塞ぐと、**復旧手段まで失う**
        var (targetId, templateId) = await SeedAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().PreviewAsync(
                targetId, templateId, "web", new() { ["HTTP_PORT"] = port }));

        Assert.Contains("使えません", error.Message);
    }

    [Theory]
    [InlineData("22")]   // 特権ポートは下限で弾く(SSHもここで守られる)
    [InlineData("80")]
    [InlineData("70000")]
    [InlineData("なんでもない")]
    public async Task 範囲外のポートは受け付けない(string port)
    {
        var (targetId, templateId) = await SeedAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().PreviewAsync(
                targetId, templateId, "web", new() { ["HTTP_PORT"] = port }));
    }

    [Fact]
    public async Task 展開先が未設定の対象へは展開できない()
    {
        // 監視用の接続先を流用しない。権限が違う経路を混同させない
        var (targetId, templateId) = await SeedAsync(withDeployEndpoint: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().PreviewAsync(targetId, templateId, "web", []));

        Assert.Contains("deployEndpoint", error.Message);
    }

    // --- 下見 -----------------------------------------------------------

    [Fact]
    public async Task 下見では何も作らない()
    {
        var (targetId, templateId) = await SeedAsync();

        var plan = await CreateSut().PreviewAsync(targetId, templateId, "web", []);

        Assert.False(plan.Applied);
        Assert.Empty(_adapter.PulledImages);
        Assert.Empty(_adapter.CreatedContainers);
        Assert.Empty(_db.DeployedServices);
    }

    [Fact]
    public async Task 下見に秘密値の中身を出さない()
    {
        var (targetId, templateId) = await SeedAsync();

        var plan = await CreateSut().PreviewAsync(
            targetId, templateId, "web", new() { ["ADMIN_TOKEN"] = "hunter2trustno1" });

        // 項目名は出すが、値は出さない
        Assert.Contains("ADMIN_TOKEN", plan.EnvironmentKeys);
        Assert.DoesNotContain("hunter2trustno1", JsonSerializer.Serialize(plan));
    }

    // --- 展開 -----------------------------------------------------------

    [Fact]
    public async Task 展開はイメージ取得とボリューム作成を経てコンテナを作る()
    {
        var (targetId, templateId) = await SeedAsync();

        var result = await CreateSut().DeployAsync(targetId, templateId, "web", [], userId: 1);

        Assert.Equal(DeployedServiceStatus.Stopped, result.Status);
        Assert.Equal("svc-web", result.ContainerName);
        Assert.Contains("nginx:1.27-alpine", _adapter.PulledImages);
        Assert.Contains("svc-web-html", _adapter.EnsuredVolumes);
        Assert.Single(_adapter.CreatedContainers);
    }

    [Fact]
    public async Task 展開先には展開用の接続先を使う()
    {
        // **監視用の接続先を使ってはいけない。**権限が足りず、
        // 権限があってしまう構成では二層の境界が崩れる
        var (targetId, templateId) = await SeedAsync();

        await CreateSut().DeployAsync(targetId, templateId, "web", [], userId: 1);

        Assert.All(_adapter.UsedEndpoints, e => Assert.Equal("http://deploy-proxy:2375", e));
    }

    [Fact]
    public async Task 作るコンテナは特権を持たない()
    {
        var (targetId, templateId) = await SeedAsync();

        await CreateSut().DeployAsync(targetId, templateId, "web", [], userId: 1);

        // ContainerSpec に特権を指定する術が無いことの確認。
        // アダプタ側で常に false を送る
        var spec = Assert.Single(_adapter.CreatedContainers);
        Assert.Equal("svc-web", spec.Name);
        Assert.All(spec.Volumes.Keys, v => Assert.StartsWith("svc-web-", v));
    }

    [Fact]
    public async Task ボリュームは名前付きだけでホストのパスをマウントしない()
    {
        // テンプレートの入力に "/etc" のようなホストパスを入れても、
        // ボリューム名として扱われるだけでホストはマウントされない
        var (targetId, templateId) = await SeedAsync();

        await CreateSut().DeployAsync(
            targetId, templateId, "web", new() { ["html"] = "/etc" }, userId: 1);

        var spec = Assert.Single(_adapter.CreatedContainers);
        Assert.All(spec.Volumes.Keys, v => Assert.DoesNotContain("/", v));
    }

    [Fact]
    public async Task 秘密値は展開の記録に残さない()
    {
        var (targetId, templateId) = await SeedAsync();

        var result = await CreateSut().DeployAsync(
            targetId, templateId, "web", new() { ["ADMIN_TOKEN"] = "hunter2trustno1" }, userId: 1);

        Assert.DoesNotContain("hunter2trustno1", result.InputsJson);
    }

    [Fact]
    public async Task イメージ取得に失敗したらコンテナを作らない()
    {
        var (targetId, templateId) = await SeedAsync();
        _adapter.PullSucceeds = false;

        var result = await CreateSut().DeployAsync(targetId, templateId, "web", [], userId: 1);

        Assert.Equal(DeployedServiceStatus.Failed, result.Status);
        Assert.Empty(_adapter.CreatedContainers);
    }

    // --- 削除 -----------------------------------------------------------

    [Fact]
    public async Task 稼働中のコンテナは削除できない()
    {
        // 動いているものを黙って止めない
        var (targetId, templateId) = await SeedAsync();
        var deployed = await CreateSut().DeployAsync(targetId, templateId, "web", [], userId: 1);
        _adapter.RemoveSucceeds = false;

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => CreateSut().RemoveAsync(deployed.Id, userId: 1));

        Assert.NotEqual(DeployedServiceStatus.Removed,
            (await _db.DeployedServices.SingleAsync()).Status);
    }
}

/// <summary>展開先の代わり。呼ばれた接続先と操作を記録する。</summary>
public class FakeDeploymentAdapter : IDeploymentAdapter
{
    public List<string> UsedEndpoints { get; } = [];

    public List<string> PulledImages { get; } = [];

    public List<string> EnsuredVolumes { get; } = [];

    public List<ContainerSpec> CreatedContainers { get; } = [];

    public bool PullSucceeds { get; set; } = true;

    public bool RemoveSucceeds { get; set; } = true;

    public Task<AdapterConnectionResult> TestConnectionAsync(
        string endpoint, CancellationToken ct = default)
    {
        UsedEndpoints.Add(endpoint);
        return Task.FromResult(new AdapterConnectionResult(true, "ok"));
    }

    public Task<ImagePullResult> PullImageAsync(
        string endpoint, string image, CancellationToken ct = default)
    {
        UsedEndpoints.Add(endpoint);
        if (!PullSucceeds)
        {
            return Task.FromResult(new ImagePullResult(false, "取得に失敗しました。", null));
        }

        PulledImages.Add(image);
        return Task.FromResult(new ImagePullResult(true, "ok", null));
    }

    public Task<AdapterConnectionResult> EnsureVolumeAsync(
        string endpoint, string name, CancellationToken ct = default)
    {
        UsedEndpoints.Add(endpoint);
        EnsuredVolumes.Add(name);
        return Task.FromResult(new AdapterConnectionResult(true, "ok"));
    }

    public Task<AdapterConnectionResult> EnsureNetworkAsync(
        string endpoint, string name, CancellationToken ct = default)
    {
        UsedEndpoints.Add(endpoint);
        return Task.FromResult(new AdapterConnectionResult(true, "ok"));
    }

    public Task<AdapterConnectionResult> CreateContainerAsync(
        string endpoint, ContainerSpec spec, CancellationToken ct = default)
    {
        UsedEndpoints.Add(endpoint);
        CreatedContainers.Add(spec);
        return Task.FromResult(new AdapterConnectionResult(true, "ok"));
    }

    public Task<AdapterConnectionResult> RemoveContainerAsync(
        string endpoint, string containerNameOrId, CancellationToken ct = default)
    {
        UsedEndpoints.Add(endpoint);
        return Task.FromResult(RemoveSucceeds
            ? new AdapterConnectionResult(true, "ok")
            : new AdapterConnectionResult(false, "稼働中のため削除できません。"));
    }
}
