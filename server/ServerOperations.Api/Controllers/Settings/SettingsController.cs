using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Settings;

[ApiController]
[Route("api/v1/settings")]
[Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
public class SettingsController(
    ISettingsService settingsService,
    ServerOperations.Core.Services.IRetentionService retentionService) : ControllerBase
{
    /// <summary>現在の保持設定で削除される件数と容量見込みを返す(削除は行わない)。</summary>
    [HttpGet("retention/preview")]
    public async Task<ActionResult<ApiResponse<DTOs.Operations.RetentionPreviewDto>>> PreviewRetention(
        CancellationToken ct)
    {
        var settings = await settingsService.GetRetentionAsync(ct);
        var preview = await retentionService.PreviewAsync(ToPolicy(settings), ct);

        return Ok(ApiResponse<DTOs.Operations.RetentionPreviewDto>.Ok(new()
        {
            MetricSnapshots = preview.MetricSnapshots,
            IncidentLogs = preview.IncidentLogs,
            Incidents = preview.Incidents,
            AuditLogs = preview.AuditLogs,
            Notifications = preview.Notifications,
            HealthChecks = preview.HealthChecks,
            Total = preview.Total,
            EstimatedBytes = preview.EstimatedBytes,
        }, TraceId()));
    }

    /// <summary>
    /// 保持設定から削除ポリシーを作る。custom以外はプロファイルの既定値を使う。
    /// </summary>
    private static ServerOperations.Core.Services.RetentionPolicy ToPolicy(RetentionSettingsDto settings) =>
        settings.Profile == "custom"
            ? new ServerOperations.Core.Services.RetentionPolicy(
                settings.MetricsDays, settings.LogsDays, settings.IncidentsDays, settings.AuditDays)
            : ServerOperations.Core.Services.RetentionPolicy.FromProfile(settings.Profile);

    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<ProfileSettingsDto>>> GetProfile(CancellationToken ct)
    {
        var result = await settingsService.GetProfileAsync(ct);
        return Ok(ApiResponse<ProfileSettingsDto>.Ok(result, TraceId()));
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<ProfileSettingsDto>>> UpdateProfile(
        [FromBody] ProfileSettingsDto request, CancellationToken ct)
    {
        var result = await settingsService.UpdateProfileAsync(request, ct);
        return Ok(ApiResponse<ProfileSettingsDto>.Ok(result, TraceId()));
    }

    [HttpGet("retention")]
    public async Task<ActionResult<ApiResponse<RetentionSettingsDto>>> GetRetention(CancellationToken ct)
    {
        var result = await settingsService.GetRetentionAsync(ct);
        return Ok(ApiResponse<RetentionSettingsDto>.Ok(result, TraceId()));
    }

    [HttpPut("retention")]
    public async Task<ActionResult<ApiResponse<RetentionSettingsDto>>> UpdateRetention(
        [FromBody] RetentionSettingsDto request, CancellationToken ct)
    {
        var result = await settingsService.UpdateRetentionAsync(request, ct);
        return Ok(ApiResponse<RetentionSettingsDto>.Ok(result, TraceId()));
    }

    /// <summary>
    /// 通知設定。秘密値(SMTPパスワード・FCMサービスアカウント)は含まない。
    /// </summary>
    [HttpGet("notification")]
    public async Task<ActionResult<ApiResponse<NotificationSettingsDto>>> GetNotification(
        CancellationToken ct)
    {
        var result = await settingsService.GetNotificationAsync(ct);
        return Ok(ApiResponse<NotificationSettingsDto>.Ok(result, TraceId()));
    }

    [HttpPut("notification")]
    public async Task<ActionResult<ApiResponse<NotificationSettingsDto>>> UpdateNotification(
        [FromBody] NotificationSettingsDto request, CancellationToken ct)
    {
        var result = await settingsService.UpdateNotificationAsync(request, ct);
        return Ok(ApiResponse<NotificationSettingsDto>.Ok(result, TraceId()));
    }

    /// <summary>
    /// 保存済みの設定と宛先へテスト通知を送る。
    /// 宛先は保存済みのものだけを使う。任意の宛先へ送れる口は用意しない
    /// (踏み台としてメールを送らせる経路になるため)。
    /// </summary>
    [HttpPost("notification/test")]
    public async Task<ActionResult<ApiResponse<List<DTOs.Operations.NotificationTestResultDto>>>>
        TestNotification(CancellationToken ct)
    {
        var results = await settingsService.SendTestNotificationAsync(ct);
        return Ok(ApiResponse<List<DTOs.Operations.NotificationTestResultDto>>.Ok(results, TraceId()));
    }

    /// <summary>
    /// バックアップ設定。アクセスキー・シークレットキーは含まない。
    /// </summary>
    [HttpGet("backup-settings")]
    public async Task<ActionResult<ApiResponse<BackupSettingsDto>>> GetBackup(CancellationToken ct)
    {
        var result = await settingsService.GetBackupAsync(ct);
        return Ok(ApiResponse<BackupSettingsDto>.Ok(result, TraceId()));
    }

    [HttpPut("backup-settings")]
    public async Task<ActionResult<ApiResponse<BackupSettingsDto>>> UpdateBackup(
        [FromBody] BackupSettingsDto request, CancellationToken ct)
    {
        var result = await settingsService.UpdateBackupAsync(request, ct);
        return Ok(ApiResponse<BackupSettingsDto>.Ok(result, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
