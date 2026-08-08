using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Services.Deployment;

namespace ServerOperations.Api.Controllers.Operations;

/// <summary>
/// テンプレートからのサービス展開。
///
/// **第2層(人が明示的に起動する運用操作)。**
/// 診断・AI・ルールからは到達できない。
///
/// 展開は既存の環境を変える操作のため、バックアップの復元と同じ形にしてある。
/// 管理者 + MFA再認証、下見、実行時の確認、監査。
/// </summary>
[ApiController]
[Route("api/v1/services")]
[Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
public class ServiceDeploymentController(
    AppDbContext db,
    IServiceDeploymentService deployment,
    IAuditService audit,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    [HttpGet("templates")]
    public async Task<ActionResult<ApiResponse<List<ServiceTemplateDto>>>> GetTemplates(
        CancellationToken ct)
    {
        var items = await db.ServiceTemplates
            .Include(t => t.Inputs)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

        return Ok(ApiResponse<List<ServiceTemplateDto>>.Ok(
            items.Select(ServiceTemplateDto.From).ToList(), TraceId()));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<DeployedServiceDto>>>> GetDeployed(
        CancellationToken ct)
    {
        var items = await db.DeployedServices
            .OrderByDescending(s => s.DeployedAt)
            .Take(200)
            .ToListAsync(ct);

        return Ok(ApiResponse<List<DeployedServiceDto>>.Ok(
            items.Select(DeployedServiceDto.From).ToList(), TraceId()));
    }

    /// <summary>展開の下見。**何も作らない。**</summary>
    [HttpPost("deploy-preview")]
    public async Task<ActionResult<ApiResponse<DeploymentPlanDto>>> Preview(
        [FromBody] DeployServiceRequest request, CancellationToken ct)
    {
        try
        {
            var plan = await deployment.PreviewAsync(
                request.TargetId, request.TemplateId, request.Name, request.Inputs, ct);

            return Ok(ApiResponse<DeploymentPlanDto>.Ok(DeploymentPlanDto.From(plan), TraceId()));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ApiResponse<DeploymentPlanDto>.Fail(
                "DEPLOY_PREVIEW_FAILED", ex.Message, TraceId()));
        }
    }

    [HttpPost("deploy")]
    public async Task<ActionResult<ApiResponse<DeployedServiceDto>>> Deploy(
        [FromBody] DeployServiceRequest request, CancellationToken ct)
    {
        // 取り違え防止。下見で見た名前をもう一度指定させる
        if (!string.Equals(request.Confirm, request.Name, StringComparison.Ordinal))
        {
            return BadRequest(ApiResponse<DeployedServiceDto>.Fail(
                "DEPLOY_NOT_CONFIRMED",
                "確認のため、展開するサービス名をもう一度指定してください。", TraceId()));
        }

        try
        {
            var result = await deployment.DeployAsync(
                request.TargetId, request.TemplateId, request.Name,
                request.Inputs, currentUser.UserId, ct);

            await audit.RecordAsync(
                "service.deploy", "DeployedService", result.Id.ToString(),
                result.Status == DeployedServiceStatus.Failed
                    ? AuditResult.Failure : AuditResult.Success,
                actorUserId: currentUser.UserId, actorName: currentUser.Username,
                // 秘密値は監査にも残さない。何を展開したかだけ残す
                details: $"name={result.Name} image={result.Image} container={result.ContainerName}",
                ct: ct);

            return Ok(ApiResponse<DeployedServiceDto>.Ok(
                DeployedServiceDto.From(result), TraceId()));
        }
        catch (InvalidOperationException ex)
        {
            await audit.RecordAsync(
                "service.deploy", "DeployedService", request.Name, AuditResult.Failure,
                actorUserId: currentUser.UserId, actorName: currentUser.Username,
                details: ex.Message, ct: ct);

            return BadRequest(ApiResponse<DeployedServiceDto>.Fail(
                "DEPLOY_FAILED", ex.Message, TraceId()));
        }
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse<DeployedServiceDto>>> Remove(
        long id, [FromQuery] string? confirm, CancellationToken ct)
    {
        var record = await db.DeployedServices.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (record is null)
        {
            return NotFound(ApiResponse<DeployedServiceDto>.Fail(
                "NOT_FOUND", "対象の展開記録がありません。", TraceId()));
        }

        if (!string.Equals(confirm, record.Name, StringComparison.Ordinal))
        {
            return BadRequest(ApiResponse<DeployedServiceDto>.Fail(
                "REMOVE_NOT_CONFIRMED",
                "確認のため、削除するサービス名を指定してください。", TraceId()));
        }

        try
        {
            var result = await deployment.RemoveAsync(id, currentUser.UserId, ct);

            await audit.RecordAsync(
                "service.remove", "DeployedService", id.ToString(), AuditResult.Success,
                actorUserId: currentUser.UserId, actorName: currentUser.Username,
                details: $"name={result.Name} container={result.ContainerName}", ct: ct);

            return Ok(ApiResponse<DeployedServiceDto>.Ok(
                DeployedServiceDto.From(result), TraceId()));
        }
        catch (InvalidOperationException ex)
        {
            await audit.RecordAsync(
                "service.remove", "DeployedService", id.ToString(), AuditResult.Failure,
                actorUserId: currentUser.UserId, actorName: currentUser.Username,
                details: ex.Message, ct: ct);

            return BadRequest(ApiResponse<DeployedServiceDto>.Fail(
                "REMOVE_FAILED", ex.Message, TraceId()));
        }
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
