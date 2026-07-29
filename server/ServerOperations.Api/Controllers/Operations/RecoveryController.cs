using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/incidents/{incidentId:long}")]
[Authorize]
public class RecoveryController(IRecoveryService recoveryService) : ControllerBase
{
    /// <summary>復旧の承認・却下。管理者 + MFA再認証が必要。</summary>
    [HttpPost("approvals")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<ApprovalDto>>> CreateApproval(
        long incidentId, [FromBody] CreateApprovalRequest request, CancellationToken ct)
    {
        var result = await recoveryService.CreateApprovalAsync(incidentId, request, ct);
        return Ok(ApiResponse<ApprovalDto>.Ok(result, TraceId()));
    }

    [HttpGet("approvals")]
    public async Task<ActionResult<ApiResponse<List<ApprovalDto>>>> GetApprovals(
        long incidentId, CancellationToken ct)
    {
        var result = await recoveryService.GetApprovalsAsync(incidentId, ct);
        return Ok(ApiResponse<List<ApprovalDto>>.Ok(result, TraceId()));
    }

    /// <summary>
    /// 復旧アクションの要求。副作用のある操作にはIdempotency-Keyヘッダーが必要。
    /// 実行はWorkerが行い、High操作は受け付けない。
    /// </summary>
    [HttpPost("recovery-actions")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<RecoveryActionDto>>> CreateAction(
        long incidentId,
        [FromBody] CreateRecoveryActionRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var result = await recoveryService.RequestActionAsync(incidentId, request, idempotencyKey, ct);
        return Ok(ApiResponse<RecoveryActionDto>.Ok(result, TraceId()));
    }

    [HttpGet("recovery-actions")]
    public async Task<ActionResult<ApiResponse<List<RecoveryActionDto>>>> GetActions(
        long incidentId, CancellationToken ct)
    {
        var result = await recoveryService.GetActionsAsync(incidentId, ct);
        return Ok(ApiResponse<List<RecoveryActionDto>>.Ok(result, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
