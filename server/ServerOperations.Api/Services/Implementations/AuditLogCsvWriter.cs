using System.Globalization;
using System.Text;
using ServerOperations.Api.DTOs.Settings;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>
/// 監査ログのCSV化。検証結果を論文の図表へ起こす作業のために用意する。
/// </summary>
public static class AuditLogCsvWriter
{
    public static readonly string[] Header =
    [
        "occurredAt", "actorUserId", "actorName", "ipAddress", "userAgent",
        "targetType", "targetId", "action", "result", "details", "traceId",
    ];

    public static string Write(IEnumerable<AuditLogDto> logs)
    {
        var builder = new StringBuilder();

        // ExcelがUTF-8と判別できるようBOMを付ける。無いと日本語が化ける。
        builder.Append('﻿');
        builder.AppendLine(string.Join(',', Header));

        foreach (var log in logs)
        {
            builder.AppendLine(string.Join(',', new[]
            {
                Escape(log.OccurredAt.ToString("O", CultureInfo.InvariantCulture)),
                Escape(log.ActorUserId?.ToString(CultureInfo.InvariantCulture)),
                Escape(log.ActorName),
                Escape(log.IpAddress),
                Escape(log.UserAgent),
                Escape(log.TargetType),
                Escape(log.TargetId),
                Escape(log.Action),
                Escape(log.Result),
                Escape(log.Details),
                Escape(log.TraceId),
            }));
        }

        return builder.ToString();
    }

    /// <summary>
    /// CSVの1項目を作る。区切り・引用符・改行の処理に加え、
    /// 表計算ソフトが数式として解釈する先頭文字を無害化する。
    ///
    /// User-Agentのように外部から与えられた値がそのまま入るため、
    /// =cmd|... のような値を開いた時点で実行されるのを防ぐ必要がある。
    /// </summary>
    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        var text = value;
        if (text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            // 先頭にシングルクォートを付けると、表計算ソフトは文字列として扱う
            text = "'" + text;
        }

        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
