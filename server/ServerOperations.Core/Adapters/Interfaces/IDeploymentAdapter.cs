namespace ServerOperations.Core.Adapters.Interfaces;

/// <summary>イメージの取得結果。</summary>
public record ImagePullResult(bool Success, string Message, string? ImageId);

/// <summary>展開するコンテナの指定。</summary>
public record ContainerSpec
{
    public required string Name { get; init; }

    public required string Image { get; init; }

    /// <summary>環境変数。**秘密値はここに入れず、呼び出し側で解決してから渡す。**</summary>
    public Dictionary<string, string> Environment { get; init; } = [];

    /// <summary>公開ポート。ホスト側ポート -> コンテナ側ポート。</summary>
    public Dictionary<int, int> Ports { get; init; } = [];

    /// <summary>ボリューム。ボリューム名 -> コンテナ内のパス。</summary>
    public Dictionary<string, string> Volumes { get; init; } = [];

    public string? Network { get; init; }

    public string RestartPolicy { get; init; } = "unless-stopped";

    /// <summary>メモリ上限(バイト)。0なら指定しない。</summary>
    public long MemoryLimitBytes { get; init; }

    /// <summary>この展開を行った利用者を示すラベル。監査と突き合わせる。</summary>
    public Dictionary<string, string> Labels { get; init; } = [];
}

/// <summary>
/// サービスを展開するための操作。
///
/// **監視用の <see cref="IDockerAdapter"/> とは別に定義している。**
/// 展開にはイメージ・ボリューム・ネットワーク・コンテナ作成の権限が要り、
/// これはホストのrootと同義になる。
/// 監視と同じインターフェースに混ぜると、無人で動く収集経路にまで
/// その権限が必要であるかのように見えてしまう。
///
/// 実装は展開専用の接続先(deploy-proxy)を使う。
/// 監視用の接続先(socket-proxy)は権限が足りず、ここの操作を行えない。
///
/// 設計は docs/extension-design.md の第4節にある。
/// </summary>
public interface IDeploymentAdapter
{
    /// <summary>展開先へ接続できるか。**書き込みは行わない。**</summary>
    Task<AdapterConnectionResult> TestConnectionAsync(
        string endpoint, CancellationToken ct = default);

    /// <summary>イメージを取得する。</summary>
    Task<ImagePullResult> PullImageAsync(
        string endpoint, string image, CancellationToken ct = default);

    /// <summary>ボリュームを作る。既にあれば何もしない。</summary>
    Task<AdapterConnectionResult> EnsureVolumeAsync(
        string endpoint, string name, CancellationToken ct = default);

    /// <summary>ネットワークを作る。既にあれば何もしない。</summary>
    Task<AdapterConnectionResult> EnsureNetworkAsync(
        string endpoint, string name, CancellationToken ct = default);

    /// <summary>コンテナを作る。同名が既にあれば失敗させる(黙って置き換えない)。</summary>
    Task<AdapterConnectionResult> CreateContainerAsync(
        string endpoint, ContainerSpec spec, CancellationToken ct = default);

    /// <summary>
    /// コンテナを削除する。**停止していない場合は失敗させる。**
    /// 動いているものを黙って止めない。
    /// </summary>
    Task<AdapterConnectionResult> RemoveContainerAsync(
        string endpoint, string containerNameOrId, CancellationToken ct = default);
}
