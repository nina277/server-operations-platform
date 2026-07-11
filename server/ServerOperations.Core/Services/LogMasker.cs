using System.Text.RegularExpressions;

namespace ServerOperations.Core.Services;

/// <summary>
/// ログ抜粋から秘密情報をマスクする。保存・表示・通知の前に必ず適用する。
/// </summary>
public static partial class LogMasker
{
    private const string Mask = "***";

    [GeneratedRegex(
        @"(?i)\b(password|passwd|pwd|secret|token|api[-_]?key|access[-_]?key|client[-_]?secret|authorization|auth[-_]?token|session[-_]?id|cookie)\b(\s*[=:]\s*)(""[^""]*""|'[^']*'|\S+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex KeyValueSecretPattern();

    [GeneratedRegex(@"(?i)\b(bearer|basic)\s+[A-Za-z0-9\-_.=+/]{8,}", RegexOptions.CultureInvariant)]
    private static partial Regex AuthorizationHeaderPattern();

    [GeneratedRegex(@"://([^/\s:@]+):([^/\s@]+)@", RegexOptions.CultureInvariant)]
    private static partial Regex UrlCredentialsPattern();

    public static string MaskSecrets(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }

        // Bearer/Basicトークンを先にマスクする(key=value形式のマスクがスキーム名だけを潰すのを防ぐ)
        var masked = AuthorizationHeaderPattern().Replace(content, m => $"{m.Groups[1].Value} {Mask}");
        masked = KeyValueSecretPattern().Replace(masked, m => $"{m.Groups[1].Value}{m.Groups[2].Value}{Mask}");
        masked = UrlCredentialsPattern().Replace(masked, $"://{Mask}:{Mask}@");
        return masked;
    }
}
