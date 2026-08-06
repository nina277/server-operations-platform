using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Core.Adapters.Implementations;

public class AdapterTemplateCatalog : IAdapterTemplateCatalog
{
    public const string DockerHost = "docker-host";
    public const string DockerComposeApp = "docker-compose-app";
    public const string WebSite = "web-site";

    private static readonly AdapterTemplate[] Templates =
    [
        new(
            Id: DockerHost,
            Name: "Docker Host",
            Description: "Docker Socket ProxyまたはTLS保護済みDocker APIを介してDockerホストを監視する。",
            Inputs:
            [
                new TemplateInput(
                    "endpoint", "Docker APIエンドポイント", TemplateInputType.Url, Required: true, Secret: false,
                    "Docker Socket Proxy(例: http://socket-proxy:2375)またはTLS保護済みAPI(https://host:2376)のURL。docker.sockの直接マウントは使用しない。"),
                new TemplateInput(
                    "metricsEndpoint", "ホストメトリクスURL", TemplateInputType.Url, Required: false, Secret: false,
                    "node_exporter のURL(例: http://192.168.1.20:9100/metrics)。設定するとホストのディスク使用率を収集する。読み取りのみで、ホスト上では何も実行しない。"),
            ],
            RecommendedMonitors: ["container-state", "restart-count", "log-excerpt", "cpu", "memory", "disk"],
            InitialRules: ["ContainerStopped", "MemoryPressure", "DiskPressure"],
            AllowedOperations: ["RESTART_ALLOWED_CONTAINER", "START_ALLOWED_CONTAINER", "STOP_ALLOWED_CONTAINER"],
            Capabilities:
            [
                "docker.containers.list", "docker.container.inspect", "docker.container.logs",
                "docker.container.stats",
                "docker.container.start", "docker.container.stop", "docker.container.restart",
                "host.metrics.read",
            ],
            CollectableMonitors:
            [
                MonitorKinds.ContainerState, MonitorKinds.LogExcerpt,
                MonitorKinds.ResourceUsage, MonitorKinds.DiskUsage,
            ]),
        new(
            Id: DockerComposeApp,
            Name: "Docker Compose Application",
            Description: "Dockerホスト上の特定Composeプロジェクトをアプリケーション単位で監視する。",
            Inputs:
            [
                new TemplateInput(
                    "endpoint", "Docker APIエンドポイント", TemplateInputType.Url, Required: true, Secret: false,
                    "Composeプロジェクトが動作するホストのDocker Socket ProxyまたはTLS保護済みAPIのURL。"),
                new TemplateInput(
                    "composeProject", "Composeプロジェクト名", TemplateInputType.String, Required: true, Secret: false,
                    "監視対象のDocker Composeプロジェクト名(com.docker.compose.projectラベル)。"),
            ],
            RecommendedMonitors: ["container-state", "restart-count", "log-excerpt", "cpu", "memory"],
            InitialRules: ["ContainerStopped"],
            AllowedOperations: ["RESTART_ALLOWED_CONTAINER", "START_ALLOWED_CONTAINER", "STOP_ALLOWED_CONTAINER"],
            Capabilities:
            [
                "docker.containers.list", "docker.container.inspect", "docker.container.logs",
                "docker.container.stats",
                "docker.container.start", "docker.container.stop", "docker.container.restart",
            ],
            CollectableMonitors:
                [MonitorKinds.ContainerState, MonitorKinds.LogExcerpt, MonitorKinds.ResourceUsage]),
        new(
            Id: WebSite,
            Name: "Web Site / API",
            Description: "HTTP/HTTPSのヘルスチェックによりWebサイト・APIの死活と応答時間を監視する。",
            Inputs:
            [
                new TemplateInput(
                    "url", "チェックURL", TemplateInputType.Url, Required: true, Secret: false,
                    "監視するURL(http/https)。localhost・リンクローカル・メタデータIPは登録できない。"),
                new TemplateInput(
                    "expectedStatus", "期待ステータスコード", TemplateInputType.Int, Required: false, Secret: false,
                    "正常とみなすHTTPステータスコード。", DefaultValue: "200"),
                new TemplateInput(
                    "timeoutSeconds", "タイムアウト(秒)", TemplateInputType.Int, Required: false, Secret: false,
                    "チェックのタイムアウト秒数。", DefaultValue: "10"),
                new TemplateInput(
                    "basicAuthUser", "Basic認証ユーザー", TemplateInputType.String, Required: false, Secret: false,
                    "Basic認証が必要な場合のユーザー名。"),
                new TemplateInput(
                    "basicAuthPassword", "Basic認証パスワード", TemplateInputType.String, Required: false, Secret: true,
                    "Basic認証が必要な場合のパスワード。暗号化して保存される。"),
            ],
            RecommendedMonitors: ["http-status", "http-latency"],
            InitialRules: ["HttpUnavailable"],
            AllowedOperations: ["RECHECK_HTTP_HEALTH"],
            Capabilities: ["http.check"],
            // 死活と応答時間は同じ1回の呼び出しで取れるため、まとめて1つの単位にする
            CollectableMonitors: [MonitorKinds.HttpCheck]),
    ];

    public IReadOnlyList<AdapterTemplate> GetAll() => Templates;

    public AdapterTemplate? Find(string templateId) =>
        Templates.FirstOrDefault(t => t.Id == templateId);
}
