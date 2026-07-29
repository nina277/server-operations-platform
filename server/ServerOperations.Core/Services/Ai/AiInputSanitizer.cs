using System.Text.RegularExpressions;

namespace ServerOperations.Core.Services.Ai;

/// <summary>
/// 外部AIへ送る入力の匿名化と縮小。
/// トークン・パスワード・Cookie・APIキー・IPアドレス・内部ホスト名・メールアドレスをマスクし、
/// 重複行を除去して文字数上限へ収める。
/// </summary>
public static partial class AiInputSanitizer
{
    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv4Pattern();

    [GeneratedRegex(@"\b(?:[0-9a-fA-F]{1,4}:){2,7}[0-9a-fA-F]{1,4}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Ipv6Pattern();

    [GeneratedRegex(@"[\w.+-]+@[\w-]+\.[\w.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    /// <summary>内部ホスト名(.local / .internal / .lan / 単一ラベルのhost名など)。</summary>
    [GeneratedRegex(@"\b[\w-]+\.(?:local|internal|lan|home|arpa)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InternalHostPattern();

    [GeneratedRegex(@"(?i)\bcookie\b\s*[:=]\s*\S+", RegexOptions.CultureInvariant)]
    private static partial Regex CookiePattern();

    /// <summary>JWTらしき文字列。</summary>
    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]+",
        RegexOptions.CultureInvariant)]
    private static partial Regex JwtPattern();

    /// <summary>長いランダム文字列(APIキー等)。</summary>
    [GeneratedRegex(@"\b[A-Za-z0-9_-]{32,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex LongTokenPattern();

    /// <summary>
    /// AI送信用に匿名化する。LogMaskerのマスク(key=value、Bearer、URL資格情報)も適用する。
    /// </summary>
    public static string Anonymize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        // 先に一般的な秘密情報をマスクする
        var text = LogMasker.MaskSecrets(input);

        text = CookiePattern().Replace(text, "cookie=<REDACTED>");
        text = JwtPattern().Replace(text, "<JWT>");
        text = EmailPattern().Replace(text, "<EMAIL>");
        text = InternalHostPattern().Replace(text, "<HOST>");
        text = Ipv6Pattern().Replace(text, "<IPV6>");
        text = Ipv4Pattern().Replace(text, "<IP>");

        // 最後に長いランダム文字列をマスクする(上のプレースホルダーは短いため影響しない)
        text = LongTokenPattern().Replace(text, "<TOKEN>");

        return text;
    }

    /// <summary>
    /// 重複行を除去し、文字数上限へ収める。
    /// 同一行が続くログを圧縮して、限られた入力枠を有効に使う。
    /// </summary>
    public static string Reduce(string input, int maxCharacters)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var lines = input.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var result = new List<string>();
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (seen.TryGetValue(line, out var index))
            {
                // 既出の行は件数を数え、行自体は増やさない
                seen[line] = index;
                continue;
            }

            seen[line] = result.Count;
            result.Add(line);
        }

        var reduced = string.Join('\n', result);
        return reduced.Length <= maxCharacters ? reduced : reduced[..maxCharacters];
    }

    /// <summary>匿名化と縮小をまとめて適用する。</summary>
    public static string Prepare(string? input, int maxCharacters) =>
        Reduce(Anonymize(input), maxCharacters);
}
