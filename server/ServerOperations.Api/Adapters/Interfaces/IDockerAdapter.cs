namespace ServerOperations.Api.Adapters.Interfaces;

/// <summary>接続試験・収集の共通結果。資格情報を含めてはならない。</summary>
public record AdapterConnectionResult(
    bool Success,
    string Message,
    long? LatencyMs = null,
    string? Detail = null);

public interface IDockerAdapter
{
    /// <summary>
    /// Docker APIへの接続試験。エンドポイントは事前にEndpointValidatorで検証済みであること。
    /// </summary>
    Task<AdapterConnectionResult> TestConnectionAsync(string endpoint, CancellationToken ct = default);
}
