using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services.Deployment;

/// <summary>
/// 初期投入するサービステンプレート。
///
/// **すべて版を固定してある。**latest にすると、同じテンプレートから展開しても
/// 時期によって別のものが動く。
///
/// 既定ルールと同じく、**版を上げてテンプレートが増えたときも
/// 足りない分だけ後から投入する。**利用者が消したものは復活させない。
/// </summary>
public static class DefaultServiceTemplates
{
    public static List<ServiceTemplate> Missing(IEnumerable<string> existingKeys, DateTime nowUtc)
    {
        var existing = new HashSet<string>(existingKeys, StringComparer.Ordinal);
        return Create(nowUtc).Where(t => !existing.Contains(t.Key)).ToList();
    }

    public static List<ServiceTemplate> Create(DateTime nowUtc) =>
    [
        Template(nowUtc, "uptime-kuma", "Uptime Kuma", "louislam/uptime-kuma:1.23.13",
            "外形監視。このシステムとは別に、外から死活を見るために置くことが多い。", 512,
            [
                Port("HTTP_PORT", "公開ポート", 3001, "3001"),
                Volume("data", "データ", "/app/data"),
            ]),

        Template(nowUtc, "gitea", "Gitea", "gitea/gitea:1.22.6", "自分用のGitホスティング。", 1024,
            [
                Port("HTTP_PORT", "公開ポート", 3000, "3000"),
                Volume("data", "データ", "/data"),
                Text("GITEA__database__DB_TYPE", "データベース種別", "sqlite3",
                    "小規模ならsqlite3のままでよい。"),
            ]),

        Template(nowUtc, "vaultwarden", "Vaultwarden", "vaultwarden/server:1.32.7",
            "パスワード管理。**LAN内に閉じて使うこと。**", 512,
            [
                Port("HTTP_PORT", "公開ポート", 80, "8222"),
                Volume("data", "データ", "/data"),
                Secret("ADMIN_TOKEN", "管理トークン",
                    "管理画面用。空にすると管理画面が無効になる。"),
            ]),

        Template(nowUtc, "redis", "Redis", "redis:7.4-alpine", "キャッシュ・キュー用。", 256,
            [
                Port("HTTP_PORT", "公開ポート", 6379, "6379"),
                Volume("data", "データ", "/data"),
            ]),

        Template(nowUtc, "nginx-static", "nginx (静的配信)", "nginx:1.27-alpine",
            "静的ファイルの配信。動作確認にも使える。", 128,
            [
                Port("HTTP_PORT", "公開ポート", 80, "8081"),
                Volume("html", "配信するファイル", "/usr/share/nginx/html"),
            ]),
    ];

    private static ServiceTemplate Template(
        DateTime now, string key, string name, string image, string description,
        int memoryMb, ServiceTemplateInput[] inputs)
    {
        for (var i = 0; i < inputs.Length; i++)
        {
            inputs[i].SortOrder = i;
        }

        return new ServiceTemplate
        {
            Key = key,
            Name = name,
            Image = image,
            Description = description,
            MemoryLimitMb = memoryMb,
            IsBuiltIn = true,
            CreatedAt = now,
            UpdatedAt = now,
            Inputs = inputs,
        };
    }

    private static ServiceTemplateInput Port(
        string key, string label, int containerPort, string defaultHostPort) => new()
    {
        Key = key, Label = label, Type = ServiceInputType.Port,
        ContainerPort = containerPort, DefaultValue = defaultHostPort, Required = true,
    };

    private static ServiceTemplateInput Volume(string key, string label, string containerPath) => new()
    {
        Key = key, Label = label, Type = ServiceInputType.Volume,
        ContainerPath = containerPath, DefaultValue = key, Required = true,
    };

    private static ServiceTemplateInput Text(
        string key, string label, string? defaultValue, string? description) => new()
    {
        Key = key, Label = label, Type = ServiceInputType.Text,
        DefaultValue = defaultValue, Description = description, Required = false,
    };

    private static ServiceTemplateInput Secret(string key, string label, string? description) => new()
    {
        Key = key, Label = label, Type = ServiceInputType.Secret,
        Description = description, Required = false,
    };
}
