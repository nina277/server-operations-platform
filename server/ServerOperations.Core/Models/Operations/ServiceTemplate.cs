namespace ServerOperations.Core.Models.Operations;

/// <summary>テンプレートの入力欄の種別。</summary>
public enum ServiceInputType
{
    Text = 0,
    Port = 1,
    /// <summary>作成するボリューム。値はコンテナ内のパスではなくボリューム名の一部。</summary>
    Volume = 2,
    /// <summary>秘密値。**画面にも展開記録にも保存しない。**</summary>
    Secret = 3,
}

/// <summary>
/// サービステンプレートの入力欄。
/// 展開時に利用者が値を入れ、コンテナの環境変数・ポート・ボリュームへ割り当てる。
/// </summary>
public class ServiceTemplateInput
{
    public long Id { get; set; }

    public long TemplateId { get; set; }

    public ServiceTemplate? Template { get; set; }

    /// <summary>環境変数名、またはポート/ボリュームの識別子。</summary>
    public required string Key { get; set; }

    public required string Label { get; set; }

    public ServiceInputType Type { get; set; }

    public string? DefaultValue { get; set; }

    public bool Required { get; set; } = true;

    public string? Description { get; set; }

    /// <summary>Volume種別のとき、コンテナ内のマウント先。</summary>
    public string? ContainerPath { get; set; }

    /// <summary>Port種別のとき、コンテナ側のポート。</summary>
    public int? ContainerPort { get; set; }

    public int SortOrder { get; set; }
}

/// <summary>
/// サービステンプレート。1つのコンテナを展開するための定義。
///
/// **Composeファイルを丸ごと受け取る形にはしていない。**
/// Composeを受け取ると privileged・ホストのマウント・任意のネットワーク設定まで
/// 指定できてしまい、テンプレートを登録できる者がホストを取れる。
/// 展開できる形を「1コンテナ + 環境変数 + ポート + 名前付きボリューム」に限り、
/// 危険な指定はそもそも表現できないようにしてある。
///
/// 複数コンテナが要るものは、テンプレートを分けて順に展開する。
/// </summary>
public class ServiceTemplate
{
    public long Id { get; set; }

    /// <summary>識別子(英数字とハイフン)。</summary>
    public required string Key { get; set; }

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>イメージ。**版を必ず指定する**(latestは受け付けない)。</summary>
    public required string Image { get; set; }

    /// <summary>メモリ上限(MB)。0なら指定しない。</summary>
    public int MemoryLimitMb { get; set; }

    /// <summary>初期投入したものか。組み込みは削除できない。</summary>
    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public long? CreatedByUserId { get; set; }

    public ICollection<ServiceTemplateInput> Inputs { get; set; } = [];
}

public enum DeployedServiceStatus
{
    Deploying = 0,
    Running = 1,
    Stopped = 2,
    Failed = 3,
    Removed = 4,
}

/// <summary>
/// 展開したサービスの記録。
/// **秘密値は保存しない。**入力のうちSecret種別は記録から除く。
/// </summary>
public class DeployedService
{
    public long Id { get; set; }

    public required string Name { get; set; }

    /// <summary>展開先の監視対象。展開先の接続情報はここから引く。</summary>
    public long TargetId { get; set; }

    public MonitoringTarget? Target { get; set; }

    public long? TemplateId { get; set; }

    public ServiceTemplate? Template { get; set; }

    /// <summary>実際に作られたコンテナ名。</summary>
    public required string ContainerName { get; set; }

    /// <summary>展開時のイメージ(版つき)。後から何を入れたか辿れるようにする。</summary>
    public required string Image { get; set; }

    public DeployedServiceStatus Status { get; set; }

    /// <summary>秘密値を除いた入力値。</summary>
    public string InputsJson { get; set; } = "{}";

    public string? Message { get; set; }

    public DateTime DeployedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public long? DeployedByUserId { get; set; }
}
