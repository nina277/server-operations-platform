using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services.Backup;

namespace ServerOperations.Api.Controllers.Settings;

[ApiController]
[Route("api/v1/settings/backup")]
[Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
public class BackupController(
    IBackupService backupService,
    IBackupRunRepository runs,
    IAuditService audit,
    ICurrentUserAccessor currentUser) : ControllerBase
{
    /// <summary>保存先への接続試験(書き込みは行わない)。</summary>
    [HttpPost("test-connection")]
    public async Task<ActionResult<ApiResponse<ConnectionTestResultDto>>> TestConnection(CancellationToken ct)
    {
        var result = await backupService.TestConnectionAsync(ct);

        await audit.RecordAsync(
            "backup.test_connection", "Backup", null,
            result.Success ? AuditResult.Success : AuditResult.Failure,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: result.Message, ct: ct);

        return Ok(ApiResponse<ConnectionTestResultDto>.Ok(new ConnectionTestResultDto
        {
            Success = result.Success,
            Message = result.Message,
            LatencyMs = result.LatencyMs,
            Detail = result.Detail,
        }, TraceId()));
    }

    /// <summary>バックアップを手動実行する。</summary>
    [HttpPost("run")]
    public async Task<ActionResult<ApiResponse<BackupRunDto>>> Run(CancellationToken ct)
    {
        var run = await backupService.RunAsync(currentUser.UserId, ct);

        await audit.RecordAsync(
            "backup.run", "Backup", run.Id.ToString(),
            run.Status == Core.Models.Settings.BackupStatus.Succeeded
                ? AuditResult.Success : AuditResult.Failure,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: run.Message, ct: ct);

        return Ok(ApiResponse<BackupRunDto>.Ok(BackupRunDto.From(run), TraceId()));
    }

    [HttpGet("runs")]
    public async Task<ActionResult<ApiResponse<List<BackupRunDto>>>> GetRuns(
        [FromQuery] int limit = 20, CancellationToken ct = default)
    {
        var items = await runs.GetRecentAsync(limit, ct);
        return Ok(ApiResponse<List<BackupRunDto>>.Ok(
            items.Select(BackupRunDto.From).ToList(), TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
