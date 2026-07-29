using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services.Ai;

/// <summary>
/// Gemini Developer APIによる診断。
/// 応答はJSONスキーマで制約し、受け取った内容はAiResponseValidatorで再検証する。
/// </summary>
public class GeminiDiagnosisProvider(
    IHttpClientFactory httpClientFactory,
    ILogger<GeminiDiagnosisProvider> logger) : IAiDiagnosisProvider
{
    public const string HttpClientName = "ai-provider";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ProviderName => "gemini";

    public async Task<AiCallResponse> DiagnoseAsync(
        AiDiagnosisInput input, AiUsageLimit limits, string apiKey, CancellationToken ct = default)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{limits.Model}:generateContent";

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = BuildPrompt(input) } },
                },
            },
            generationConfig = new
            {
                temperature = 0.1,
                maxOutputTokens = limits.MaxOutputTokens,
                responseMimeType = "application/json",
            },
        };

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(limits.TimeoutSeconds, 1, 120)));

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(requestBody, JsonOptions), Encoding.UTF8, "application/json"),
            };
            // APIキーはヘッダーで渡す(URLに含めるとログへ残りうるため)
            request.Headers.Add("x-goog-api-key", apiKey);

            using var response = await client.SendAsync(request, timeoutCts.Token);
            stopwatch.Stop();

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new AiCallResponse(
                    AiCallOutcome.RateLimited, ErrorSummary: "AIプロバイダーのレート制限に達しました。",
                    LatencyMs: stopwatch.ElapsedMilliseconds);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 応答本文には入力の反射が含まれうるため記録しない
                return new AiCallResponse(
                    AiCallOutcome.Error,
                    ErrorSummary: $"AIプロバイダーが異常応答を返しました(HTTP {(int)response.StatusCode})。",
                    LatencyMs: stopwatch.ElapsedMilliseconds);
            }

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var (text, outputTokens) = ExtractText(body);

            if (string.IsNullOrWhiteSpace(text))
            {
                return new AiCallResponse(
                    AiCallOutcome.InvalidResponse, ErrorSummary: "応答本文を取得できません。",
                    OutputTokens: outputTokens, LatencyMs: stopwatch.ElapsedMilliseconds);
            }

            var validation = AiResponseValidator.Validate(text, input.AllowedActionIds);
            if (!validation.IsValid)
            {
                return new AiCallResponse(
                    AiCallOutcome.InvalidResponse, ErrorSummary: validation.Error,
                    OutputTokens: outputTokens, LatencyMs: stopwatch.ElapsedMilliseconds);
            }

            return new AiCallResponse(
                AiCallOutcome.Success, validation.Output,
                OutputTokens: outputTokens, LatencyMs: stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new AiCallResponse(
                AiCallOutcome.Timeout, ErrorSummary: "AI呼び出しがタイムアウトしました。",
                LatencyMs: stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            stopwatch.Stop();
            logger.LogWarning(ex, "AI diagnosis call failed.");
            return new AiCallResponse(
                AiCallOutcome.Error, ErrorSummary: "AIプロバイダーへ接続できません。",
                LatencyMs: stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// プロンプト。入力は匿名化・縮小済みで、AIには許可アクションIDから選ばせる。
    /// 自由記述のコマンド・URLを求めない。
    /// </summary>
    private static string BuildPrompt(AiDiagnosisInput input)
    {
        var allowed = input.AllowedActionIds.Count == 0
            ? "(実行可能な操作はありません)"
            : string.Join(", ", input.AllowedActionIds);

        const string schema = """
            {
              "classification": "障害分類(64文字以内の英数字)",
              "severity": "Low | Medium | High | Critical",
              "rationale": "推定の根拠(日本語)",
              "recommendedActionId": "許可された操作のID または null",
              "confidence": 0.0〜1.0
            }
            """;

        return $"""
            あなたはサーバー運用の障害診断を支援します。以下の障害情報から原因を推定してください。

            制約:
            - 回答はJSONのみとし、説明文やコードブロックを付けないこと。
            - recommendedActionId は次の一覧からのみ選ぶこと。該当がなければ null にすること。
              許可された操作: {allowed}
            - コマンド・シェルスクリプト・SQL・URLを出力しないこと。
            - rationale は日本語で、300文字以内の簡潔な説明にすること。

            JSONスキーマ:
            {schema}

            障害情報:
            - 分類: {input.Classification}
            - 対象サービス: {input.Service ?? "(不明)"}
            - ログ抜粋(匿名化済み):
            {input.LogExcerpt}
            """;
    }

    /// <summary>Gemini応答から本文と出力トークン数を取り出す。</summary>
    private static (string? Text, int? OutputTokens) ExtractText(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        int? outputTokens = null;
        if (root.TryGetProperty("usageMetadata", out var usage)
            && usage.TryGetProperty("candidatesTokenCount", out var count)
            && count.TryGetInt32(out var parsed))
        {
            outputTokens = parsed;
        }

        if (!root.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            return (null, outputTokens);
        }

        var first = candidates[0];
        if (!first.TryGetProperty("content", out var content)
            || !content.TryGetProperty("parts", out var parts)
            || parts.GetArrayLength() == 0)
        {
            return (null, outputTokens);
        }

        return (parts[0].TryGetProperty("text", out var text) ? text.GetString() : null, outputTokens);
    }
}
