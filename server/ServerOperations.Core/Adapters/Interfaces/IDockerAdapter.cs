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

    /// <summary>コンテナログの末尾を取得する(呼び出し側でマスクして保存すること)。</summary>
    Task<string> GetContainerLogsAsync(
        string endpoint, string containerId, int tailLines = 50, CancellationToken ct = default);
}
