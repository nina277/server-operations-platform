using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

/// <summary>
/// メンテナンス期間。計画停止中の通知と自動復旧を止める。
///
/// 自動復旧の挙動を変える操作にあたるため、運用管理者 + MFA再認証を要求する。
/// 参照も同じ扱いにする。抑止の設定は、いつ何を止めていたかという運用の情報であり、
/// 攻撃者にとっては「いつなら気づかれないか」の手掛かりになるため。
/// </summary>
[ApiController]
[Route("api/v1/maintenance-windows")]
[Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
public class MaintenanceWindowsController(IMaintenanceWindowService windows) : ControllerBase
{
    /// <summary>進行中と今後の期間を返す(終了済みは含めない)。</summary>
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<MaintenanceWindowDto>>>> GetUpcoming(
        CancellationToken ct)
    {
        var result = await windows.GetUpcomingAsync(ct);
        return Ok(ApiResponse<List<MaintenanceWindowDto>>.Ok(result, TraceId()));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<MaintenanceWindowDto>>> Create(
        [FromBody] CreateMaintenanceWindowRequest request, CancellationToken ct)
    {
        var result = await windows.CreateAsync(request, ct);
        return Ok(ApiResponse<MaintenanceWindowDto>.Ok(result, TraceId()));
    }

    /// <summary>期間を取り消す。削除ではなく取り消しとして記録を残す。</summary>
    [HttpPost("{id:long}/cancel")]
    public async Task<ActionResult<ApiResponse<MaintenanceWindowDto>>> Cancel(
        long id, CancellationToken ct)
    {
        var result = await windows.CancelAsync(id, ct);
        return Ok(ApiResponse<MaintenanceWindowDto>.Ok(result, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
