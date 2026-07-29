using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace ServerOperations.Core.Services;

/// <summary>
/// 障害署名。対象・サービス・分類・正規化ログからSHA-256を算出し、同一障害の再発を同定する。
/// </summary>
public static partial class FailureSignature
{
    [GeneratedRegex(@"\d+", RegexOptions.CultureInvariant)]
    private static partial Regex DigitsPattern();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespacePattern();

    public static string Compute(long targetId, string? service, string classification, string? logExcerpt)
    {
        var normalizedLog = Normalize(logExcerpt);
        var input = $"{targetId}|{service ?? string.Empty}|{classification}|{normalizedLog}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// ログを正規化する。タイムスタンプ・ID等の可変部分(数値)を除去し、
    /// 同種の障害が同じ署名になるようにする。
    /// </summary>
    internal static string Normalize(string? log)
    {
        if (string.IsNullOrWhiteSpace(log))
        {
            return string.Empty;
        }

        var normalized = log.ToLowerInvariant();
        normalized = DigitsPattern().Replace(normalized, "#");
        normalized = WhitespacePattern().Replace(normalized, " ").Trim();
        return normalized.Length <= 300 ? normalized : normalized[..300];
    }
}
