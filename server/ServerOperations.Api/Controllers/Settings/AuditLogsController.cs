using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Controllers.Settings;

/// <summary>
/// 監査ログの参照。運用管理者のみが読める。
/// 監査ログ自体は追記のみで、更新・削除の口は用意しない。
/// </summary>
[ApiController]
[Route("api/v1/audit-logs")]
[Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
public class AuditLogsController(IAuditLogQueryService auditLogs) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<AuditLogDto>>>> Search(
        [FromQuery] string? actorName,
        [FromQuery] string? targetType,
        [FromQuery] string? action,
        [FromQuery] string? result,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(actorName, targetType, action, result, from, to);

        var paged = await auditLogs.SearchAsync(
            filter, new PagingQuery { Page = page, PageSize = pageSize }, ct);

        return Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(paged, TraceId()));
    }

    /// <summary>
    /// 検索条件に沿った監査ログをCSVで返す。検証結果を図表に起こす作業のために用意する。
    /// 出力できる件数には上限があり、出力したこと自体も監査に残る。
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] string? actorName,
        [FromQuery] string? targetType,
        [FromQuery] string? action,
        [FromQuery] string? result,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(actorName, targetType, action, result, from, to);
        var csv = await auditLogs.ExportCsvAsync(filter, ct);

        var fileName = $"audit-logs-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv";
        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv; charset=utf-8", fileName);
    }

    /// <summary>絞り込みの選択肢。記録済みの対象種別と操作から作る。</summary>
    [HttpGet("filter-options")]
    public async Task<ActionResult<ApiResponse<AuditLogFilterOptionsDto>>> GetFilterOptions(
        CancellationToken ct)
    {
        var options = await auditLogs.GetFilterOptionsAsync(ct);
        return Ok(ApiResponse<AuditLogFilterOptionsDto>.Ok(options, TraceId()));
    }

    /// <summary>
    /// 一覧とCSV出力で同じ絞り込みを使う。片方だけ条件の扱いが違うと、
    /// 画面で見えている範囲と出力される範囲がずれる。
    /// </summary>
    private static AuditLogFilter BuildFilter(
        string? actorName, string? targetType, string? action, string? result,
        DateTime? from, DateTime? to)
    {
        AuditResult? parsedResult = null;
        if (!string.IsNullOrWhiteSpace(result))
        {
            if (!Enum.TryParse<AuditResult>(result, ignoreCase: true, out var value))
            {
                throw AppException.BadRequest("invalid_result", "結果の指定が不正です。");
            }

            parsedResult = value;
        }

        if (from is { } fromValue && to is { } toValue && fromValue > toValue)
        {
            throw AppException.BadRequest("invalid_period", "開始日時が終了日時より後になっています。");
        }

        return new AuditLogFilter
        {
            ActorName = actorName,
            TargetType = targetType,
            Action = action,
            Result = parsedResult,
            OccurredFromUtc = ToUtc(from),
            OccurredToUtc = ToUtc(to),
        };
    }

    /// <summary>保存はUTCのため、指定された日時をUTCとして扱う。</summary>
    private static DateTime? ToUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } => value,
        { Kind: DateTimeKind.Local } => value.Value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
    };

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
