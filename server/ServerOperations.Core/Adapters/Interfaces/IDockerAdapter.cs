namespace ServerOperations.Core.Adapters.Interfaces;

/// <summary>接続試験・収集の共通結果。資格情報を含めてはならない。</summary>
public record AdapterConnectionResult(
    bool Success,
    string Message,
    long? LatencyMs = null,
    string? Detail = null);

/// <summary>正規化済みのコンテナ情報。</summary>
public record ContainerInfo(
    string Id,
    string Name,
    string Image,
    string State,
    string Status,
    int RestartCount);

/// <summary>
/// コンテナのリソース使用率。算出できなかった項目はnullにする。
///
/// 取れなかったものを0で埋めてはならない。
/// 0%は「使っていない」という正常値であり、逼迫を見逃す方向へ誤る。
/// </summary>
public record ContainerStats(
    double? CpuUsagePercent,
    double? MemoryUsagePercent,
    long? MemoryUsageBytes,
    long? MemoryLimitBytes);

public interface IDockerAdapter
{
    /// <summary>
    /// Docker APIへの接続試験。エンドポイントは事前にEndpointValidatorで検証済みであること。
    /// </summary>
    Task<AdapterConnectionResult> TestConnectionAsync(string endpoint, CancellationToken ct = default);

    /// <summary>
    /// コンテナ一覧(停止中含む)。composeProject指定時はcom.docker.compose.projectラベルで絞り込む。
    /// </summary>
    Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(
        string endpoint, string? composeProject = null, CancellationToken ct = default);

    /// <summary>
    /// コンテナのCPU・メモリ使用率を1回分取得する。取得できない場合はnull。
    ///
    /// Docker APIは前回値との差分でしかCPU使用率を出せないため、この呼び出しは
    /// 内部で2周期分(約1秒)待つ。呼び出し側でコンテナ数に上限を設けること。
    /// </summary>
    Task<ContainerStats?> GetContainerStatsAsync(
        string endpoint, string containerId, CancellationToken ct = default);

    /// <summary>コンテナログの末尾を取得する(呼び出し側でマスクして保存すること)。</summary>
    Task<string> GetContainerLogsAsync(
        string endpoint, string containerId, int tailLines = 50, CancellationToken ct = default);

    /// <summary>
    /// 許可済みコンテナの開始・停止・再起動。呼び出し側で許可リスト・承認・レート制限を検証済みであること。
    /// exec・作成・削除・イメージ操作は提供しない。
    /// </summary>
    Task<AdapterConnectionResult> ControlContainerAsync(
        string endpoint, string containerNameOrId, ContainerOperation operation, CancellationToken ct = default);
}

public enum ContainerOperation
{
    Start,
    Stop,
    Restart,
}
