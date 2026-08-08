using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

/// <summary>
/// 運用実績サマリ。検知から通知までの時間、復旧の所要時間、
/// 自動実行を止めた理由の内訳を期間指定で集計する。
///
/// 読み取りのみで何も書き換えないため、ログイン済みであれば役割を問わず参照できる。
/// 集計元のインシデント・通知・復旧履歴はいずれも閲覧者が見られる情報に限る。
/// </summary>
[ApiController]
[Route("api/v1/insights")]
[Authorize]
public class OperationsInsightsController(IOperationsInsightsService insights) : ControllerBase
{
    /// <summary>既定の集計期間。指定が無ければ直近30日を見る。</summary>
    private const int DefaultRangeDays = 30;

    [HttpGet("operations")]
    public async Task<ActionResult<ApiResponse<OperationsInsightsDto>>> GetOperations(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken ct = default)
    {
        var toUtc = ToUtc(to) ?? DateTime.UtcNow;
        var fromUtc = ToUtc(from) ?? toUtc.AddDays(-DefaultRangeDays);

        var result = await insights.GetAsync(fromUtc, toUtc, ct);
        return Ok(ApiResponse<OperationsInsightsDto>.Ok(result, TraceId()));
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
