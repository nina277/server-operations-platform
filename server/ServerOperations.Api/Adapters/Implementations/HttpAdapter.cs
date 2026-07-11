using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using ServerOperations.Api.Adapters.Interfaces;

namespace ServerOperations.Api.Adapters.Implementations;

/// <summary>
/// HTTP/HTTPSヘルスチェックアダプター。リダイレクトは追跡しない(リダイレクト先への意図しない接続を防ぐ)。
/// </summary>
public class HttpAdapter(IHttpClientFactory httpClientFactory, ILogger<HttpAdapter> logger) : IHttpAdapter
{
    public const string HttpClientName = "http-adapter";

    public async Task<AdapterConnectionResult> TestConnectionAsync(
        HttpCheckOptions options, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, options.Url);

        if (!string.IsNullOrEmpty(options.BasicAuthUser))
        {
            var raw = $"{options.BasicAuthUser}:{options.BasicAuthPassword}";
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(options.TimeoutSeconds, 1, 60)));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await client.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);
            stopwatch.Stop();

            var status = (int)response.StatusCode;
            if (status == options.ExpectedStatus)
            {
                return new AdapterConnectionResult(
                    true, $"HTTP {status} を受信しました(期待どおり)。", stopwatch.ElapsedMilliseconds);
            }

            var note = status is >= 300 and < 400
                ? "リダイレクト応答ですが、リダイレクト先へは追跡しません。"
                : "期待したステータスコードと一致しません。";
            return new AdapterConnectionResult(
                false, $"HTTP {status} を受信しました(期待: {options.ExpectedStatus})。{note}",
                stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new AdapterConnectionResult(
                false, "接続がタイムアウトしました。", stopwatch.ElapsedMilliseconds);
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "HTTP connection test failed.");
            return new AdapterConnectionResult(
                false, "接続できません(到達不能または証明書エラー)。", stopwatch.ElapsedMilliseconds);
        }
    }
}
