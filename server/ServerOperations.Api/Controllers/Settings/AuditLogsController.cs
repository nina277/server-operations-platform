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

        var filter = new AuditLogFilter
        {
            ActorName = actorName,
            TargetType = targetType,
            Action = action,
            Result = parsedResult,
            OccurredFromUtc = ToUtc(from),
            OccurredToUtc = ToUtc(to),
        };

        var paged = await auditLogs.SearchAsync(
            filter, new PagingQuery { Page = page, PageSize = pageSize }, ct);

        return Ok(ApiResponse<PagedResult<AuditLogDto>>.Ok(paged, TraceId()));
    }

    /// <summary>絞り込みの選択肢。記録済みの対象種別と操作から作る。</summary>
    [HttpGet("filter-options")]
    public async Task<ActionResult<ApiResponse<AuditLogFilterOptionsDto>>> GetFilterOptions(
        CancellationToken ct)
    {
        var options = await auditLogs.GetFilterOptionsAsync(ct);
        return Ok(ApiResponse<AuditLogFilterOptionsDto>.Ok(options, TraceId()));
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
