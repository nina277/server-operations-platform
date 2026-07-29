namespace ServerOperations.Core.Adapters.Interfaces;

/// <summary>HTTPヘルスチェックの入力。資格情報はログ・応答へ出さないこと。</summary>
public record HttpCheckOptions
{
    public required string Url { get; init; }

    public int ExpectedStatus { get; init; } = 200;

    public int TimeoutSeconds { get; init; } = 10;

    public string? BasicAuthUser { get; init; }

    public string? BasicAuthPassword { get; init; }
}

public interface IHttpAdapter
{
    /// <summary>
    /// HTTP/HTTPSの接続試験。URLは事前にEndpointValidatorで検証済みであること。
    /// リダイレクトは追跡しない。
    /// </summary>
    Task<AdapterConnectionResult> TestConnectionAsync(HttpCheckOptions options, CancellationToken ct = default);
}
