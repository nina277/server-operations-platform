using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Adapters.Interfaces;

namespace ServerOperations.Core.Adapters.Implementations;

/// <summary>
/// Docker Engine API でサービスを展開する。
///
/// **展開専用の接続先(deploy-proxy)を使う。**
/// 監視用の接続先はイメージ・ボリューム・ネットワーク・コンテナ作成の権限を持たず、
/// ここの操作を行えない(経路そのもので二層の境界を守っている)。
///
/// 展開はすべて人が明示的に起動したときだけ行われる第2層の操作である。
/// 設計は docs/extension-design.md にある。
/// </summary>
public class DockerDeploymentAdapter(
    IHttpClientFactory httpClientFactory,
    ILogger<DockerDeploymentAdapter> logger) : IDeploymentAdapter
{
    public const string HttpClientName = "docker-deployment-adapter";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AdapterConnectionResult> TestConnectionAsync(
        string endpoint, CancellationToken ct = default)
    {
        // 展開に必要な権限があるかまで確かめる。
        // version だけ見ても「繋がるが何もできない」状態を見逃す
        var client = CreateClient();
        try
        {
            using var response = await client.GetAsync(Url(endpoint, "images/json?limit=1"), ct);
            if (response.StatusCode is HttpStatusCode.Forbidden)
            {
                return new AdapterConnectionResult(
                    false,
                    "接続できましたが、イメージを扱う権限がありません。"
                    + "展開用の接続先(deploy-proxy)を指定しているか確認してください。");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AdapterConnectionResult(
                    false, $"展開先が異常応答を返しました(HTTP {(int)response.StatusCode})。");
            }

            return new AdapterConnectionResult(true, "展開先へ接続できました。");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Deployment endpoint test failed.");
            return new AdapterConnectionResult(false, "展開先へ接続できません。");
        }
    }

    public async Task<ImagePullResult> PullImageAsync(
        string endpoint, string image, CancellationToken ct = default)
    {
        // タグを明示しないと latest が引かれ、次に展開したとき別のものが動く
        if (!image.Contains(':') || image.EndsWith(":latest", StringComparison.Ordinal))
        {
            return new ImagePullResult(
                false,
                "イメージは版を指定してください(latest は使わない)。"
                + "同じ設定で展開しても別のものが動く可能性があるためです。",
                null);
        }

        var client = CreateClient();
        try
        {
            // 取得は時間がかかる。応答は進捗のストリームなので読み切ってから判定する
            using var response = await client.PostAsync(
                Url(endpoint, $"images/create?fromImage={Uri.EscapeDataString(image)}"), null, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ImagePullResult(
                    false, $"イメージを取得できません(HTTP {(int)response.StatusCode})。", null);
            }

            // ストリームの途中で失敗しても200が返る。中身のerrorを見る
            if (body.Contains("\"error\"", StringComparison.Ordinal))
            {
                return new ImagePullResult(false, "イメージの取得中にエラーが返りました。", null);
            }

            return new ImagePullResult(true, $"{image} を取得しました。", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Image pull failed for {Image}.", image);
            return new ImagePullResult(false, "イメージの取得に失敗しました。", null);
        }
    }

    public Task<AdapterConnectionResult> EnsureVolumeAsync(
        string endpoint, string name, CancellationToken ct = default) =>
        EnsureAsync(endpoint, "volumes/create", new { Name = name }, "ボリューム", name, ct);

    public Task<AdapterConnectionResult> EnsureNetworkAsync(
        string endpoint, string name, CancellationToken ct = default) =>
        EnsureAsync(endpoint, "networks/create", new { Name = name }, "ネットワーク", name, ct);

    public async Task<AdapterConnectionResult> CreateContainerAsync(
        string endpoint, ContainerSpec spec, CancellationToken ct = default)
    {
        var client = CreateClient();

        var body = new Dictionary<string, object?>
        {
            ["Image"] = spec.Image,
            ["Env"] = spec.Environment.Select(e => $"{e.Key}={e.Value}").ToArray(),
            ["Labels"] = spec.Labels,
            ["ExposedPorts"] = spec.Ports.ToDictionary(p => $"{p.Value}/tcp", _ => new { }),
            ["HostConfig"] = new Dictionary<string, object?>
            {
                ["PortBindings"] = spec.Ports.ToDictionary(
                    p => $"{p.Value}/tcp",
                    p => new[] { new { HostPort = p.Key.ToString() } }),
                ["Binds"] = spec.Volumes.Select(v => $"{v.Key}:{v.Value}").ToArray(),
                ["RestartPolicy"] = new { Name = spec.RestartPolicy },
                ["Memory"] = spec.MemoryLimitBytes,
                // **特権を与えない。**テンプレートからも指定させない
                ["Privileged"] = false,
                ["NetworkMode"] = spec.Network ?? "bridge",
            },
        };

        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(
                Url(endpoint, $"containers/create?name={Uri.EscapeDataString(spec.Name)}"), content, ct);

            if (response.StatusCode is HttpStatusCode.Conflict)
            {
                // 同名を黙って置き換えない。作り直しは別の操作として扱う
                return new AdapterConnectionResult(
                    false, $"同じ名前のコンテナが既にあります({spec.Name})。");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AdapterConnectionResult(
                    false, $"コンテナを作成できません(HTTP {(int)response.StatusCode})。");
            }

            return new AdapterConnectionResult(true, $"{spec.Name} を作成しました。");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Container create failed for {Name}.", spec.Name);
            return new AdapterConnectionResult(false, "コンテナの作成に失敗しました。");
        }
    }

    public async Task<AdapterConnectionResult> RemoveContainerAsync(
        string endpoint, string containerNameOrId, CancellationToken ct = default)
    {
        var client = CreateClient();
        try
        {
            // force は付けない。**動いているものを黙って止めない。**
            // 止めるかどうかは人が別の操作として決める
            using var response = await client.DeleteAsync(
                Url(endpoint, $"containers/{Uri.EscapeDataString(containerNameOrId)}"), ct);

            if (response.StatusCode is HttpStatusCode.Conflict)
            {
                return new AdapterConnectionResult(
                    false, "稼働中のため削除できません。先に停止してください。");
            }

            if (response.StatusCode is HttpStatusCode.NotFound)
            {
                return new AdapterConnectionResult(false, "対象のコンテナがありません。");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new AdapterConnectionResult(
                    false, $"コンテナを削除できません(HTTP {(int)response.StatusCode})。");
            }

            return new AdapterConnectionResult(true, $"{containerNameOrId} を削除しました。");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Container remove failed for {Name}.", containerNameOrId);
            return new AdapterConnectionResult(false, "コンテナの削除に失敗しました。");
        }
    }

    // --- 補助 -----------------------------------------------------------

    private HttpClient CreateClient() => httpClientFactory.CreateClient(HttpClientName);

    private static Uri Url(string endpoint, string path) =>
        new(new Uri(endpoint.TrimEnd('/') + "/"), path);

    private async Task<AdapterConnectionResult> EnsureAsync(
        string endpoint, string path, object body, string label, string name, CancellationToken ct)
    {
        var client = CreateClient();
        try
        {
            using var content = new StringContent(
                JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json");
            using var response = await client.PostAsync(Url(endpoint, path), content, ct);

            // 既にある場合も成功として扱う(展開を何度行っても同じ結果にする)
            if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.Conflict)
            {
                return new AdapterConnectionResult(true, $"{label} {name} を用意しました。");
            }

            return new AdapterConnectionResult(
                false, $"{label}を作成できません(HTTP {(int)response.StatusCode})。");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "{Label} create failed for {Name}.", label, name);
            return new AdapterConnectionResult(false, $"{label}の作成に失敗しました。");
        }
    }
}
