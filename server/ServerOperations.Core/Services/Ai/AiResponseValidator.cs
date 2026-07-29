using System.Text.Json;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services.Ai;

public record AiValidationResult(bool IsValid, AiDiagnosisOutput? Output = null, string? Error = null)
{
    public static AiValidationResult Valid(AiDiagnosisOutput output) => new(true, output);

    public static AiValidationResult Invalid(string error) => new(false, Error: error);
}

/// <summary>
/// AI応答の厳格な検証。JSONスキーマ相当のDTO検証、Enum、許可アクションID、文字数を確認する。
/// 自由記述のコマンド・URL・未許可アクションを受け付けない。
/// </summary>
public static class AiResponseValidator
{
    private const int MaxRationaleLength = 1000;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// AIが返したJSONを検証する。allowedActionIdsに無いアクションは拒否する。
    /// </summary>
    public static AiValidationResult Validate(string? rawJson, IReadOnlyList<string> allowedActionIds)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return AiValidationResult.Invalid("応答が空です。");
        }

        AiResponseDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<AiResponseDto>(ExtractJson(rawJson), JsonOptions);
        }
        catch (JsonException)
        {
            return AiValidationResult.Invalid("応答がJSON形式ではありません。");
        }

        if (dto is null)
        {
            return AiValidationResult.Invalid("応答を解釈できません。");
        }

        if (string.IsNullOrWhiteSpace(dto.Classification))
        {
            return AiValidationResult.Invalid("classificationがありません。");
        }

        if (dto.Classification.Length > 64)
        {
            return AiValidationResult.Invalid("classificationが長すぎます。");
        }

        if (!Enum.TryParse<IncidentSeverity>(dto.Severity, ignoreCase: true, out var severity))
        {
            return AiValidationResult.Invalid("severityが不正です。");
        }

        if (string.IsNullOrWhiteSpace(dto.Rationale))
        {
            return AiValidationResult.Invalid("rationaleがありません。");
        }

        if (dto.Rationale.Length > MaxRationaleLength)
        {
            return AiValidationResult.Invalid("rationaleが長すぎます。");
        }

        // 推奨アクションは許可リストに含まれるIDだけを受け付ける。
        // 自由記述のコマンド・URLはここで弾かれる。
        string? recommendedActionId = null;
        if (!string.IsNullOrWhiteSpace(dto.RecommendedActionId))
        {
            var candidate = dto.RecommendedActionId.Trim();
            if (!allowedActionIds.Contains(candidate, StringComparer.Ordinal))
            {
                return AiValidationResult.Invalid(
                    $"recommendedActionIdが許可リストにありません: {Truncate(candidate, 64)}");
            }

            recommendedActionId = candidate;
        }

        if (dto.Confidence is { } confidence && (confidence < 0 || confidence > 1))
        {
            return AiValidationResult.Invalid("confidenceの範囲が不正です。");
        }

        return AiValidationResult.Valid(new AiDiagnosisOutput
        {
            Classification = dto.Classification.Trim(),
            Severity = severity,
            Rationale = dto.Rationale.Trim(),
            RecommendedActionId = recommendedActionId,
            Confidence = dto.Confidence,
        });
    }

    /// <summary>
    /// 応答からJSON部分を取り出す。コードフェンス付きで返るモデルに対応する。
    /// </summary>
    internal static string ExtractJson(string raw)
    {
        var text = raw.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
            {
                text = text[(firstNewline + 1)..];
            }

            var fenceEnd = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fenceEnd >= 0)
            {
                text = text[..fenceEnd];
            }
        }

        return text.Trim();
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];

    /// <summary>AI応答のスキーマ。未知のプロパティは無視される。</summary>
    private sealed record AiResponseDto(
        string? Classification,
        string? Severity,
        string? Rationale,
        string? RecommendedActionId,
        double? Confidence);
}
