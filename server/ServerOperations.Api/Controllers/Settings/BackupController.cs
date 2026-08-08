using Amazon.S3;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.DTOs.Settings;
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
    IBackupRestoreService restoreService,
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

    /// <summary>保存先にある世代を新しい順に返す。</summary>
    [HttpGet("generations")]
    public async Task<ActionResult<ApiResponse<List<BackupGenerationDto>>>> GetGenerations(
        CancellationToken ct)
    {
        try
        {
            var items = await restoreService.ListGenerationsAsync(ct);
            return Ok(ApiResponse<List<BackupGenerationDto>>.Ok(
                items.Select(BackupGenerationDto.From).ToList(), TraceId()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or AmazonS3Exception)
        {
            return BadRequest(ApiResponse<List<BackupGenerationDto>>.Fail(
                "BACKUP_UNAVAILABLE", ex.Message, TraceId()));
        }
    }

    /// <summary>
    /// 復元の下見。**何も変更しない。**
    ///
    /// 復元は既存のデータを書き換えるため、先に何が起きるかを見せる。
    /// </summary>
    [HttpPost("restore-preview")]
    public async Task<ActionResult<ApiResponse<BackupRestorePlanDto>>> RestorePreview(
        [FromBody] BackupRestoreRequest request, CancellationToken ct)
    {
        try
        {
            var plan = await restoreService.PreviewAsync(request.ObjectKey, ct);
            return Ok(ApiResponse<BackupRestorePlanDto>.Ok(
                BackupRestorePlanDto.From(plan), TraceId()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or AmazonS3Exception)
        {
            return BadRequest(ApiResponse<BackupRestorePlanDto>.Fail(
                "RESTORE_PREVIEW_FAILED", ex.Message, TraceId()));
        }
    }

    /// <summary>
    /// 復元を適用する。
    ///
    /// **既存のデータを書き換える破壊的な操作。**
    /// 取り違えを防ぐため、下見で示したキーを `Confirm` にもう一度指定させる。
    /// </summary>
    [HttpPost("restore")]
    public async Task<ActionResult<ApiResponse<BackupRestorePlanDto>>> Restore(
        [FromBody] BackupRestoreRequest request, CancellationToken ct)
    {
        if (!string.Equals(request.Confirm, request.ObjectKey, StringComparison.Ordinal))
        {
            await audit.RecordAsync(
                "backup.restore", "Backup", request.ObjectKey, AuditResult.Failure,
                actorUserId: currentUser.UserId, actorName: currentUser.Username,
                details: "確認の指定が一致しません。", ct: ct);

            return BadRequest(ApiResponse<BackupRestorePlanDto>.Fail(
                "RESTORE_NOT_CONFIRMED",
                "確認のため、復元するバックアップのキーをもう一度指定してください。",
                TraceId()));
        }

        try
        {
            var plan = await restoreService.RestoreAsync(request.ObjectKey, currentUser.UserId, ct);

            await audit.RecordAsync(
                "backup.restore", "Backup", request.ObjectKey, AuditResult.Success,
                actorUserId: currentUser.UserId, actorName: currentUser.Username,
                details: string.Join(
                    " / ",
                    plan.Items.Select(i =>
                        $"{i.Category}: 追加{i.Added} 更新{i.Updated} 変更なし{i.Unchanged}")),
                ct: ct);

            return Ok(ApiResponse<BackupRestorePlanDto>.Ok(
                BackupRestorePlanDto.From(plan), TraceId()));
        }
        catch (Exception ex) when (ex is InvalidOperationException or AmazonS3Exception)
        {
            await audit.RecordAsync(
                "backup.restore", "Backup", request.ObjectKey, AuditResult.Failure,
                actorUserId: currentUser.UserId, actorName: currentUser.Username,
                details: ex.Message, ct: ct);

            return BadRequest(ApiResponse<BackupRestorePlanDto>.Fail(
                "RESTORE_FAILED", ex.Message, TraceId()));
        }
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
