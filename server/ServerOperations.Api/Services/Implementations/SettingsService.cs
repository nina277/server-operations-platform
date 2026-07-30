using System.Text.Json;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class SettingsService(
    ISystemSettingRepository settings,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    Core.Services.Notifications.INotificationTestService notificationTest,
    TimeProvider timeProvider) : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ProfileSettingsDto DefaultProfile = new()
    {
        SystemName = "Server Operations Platform",
        Language = "ja",
    };

    private static readonly RetentionSettingsDto DefaultRetention = new()
    {
        Profile = "standard",
        MetricsDays = 30,
        LogsDays = 30,
        IncidentsDays = 365,
        AuditDays = 365,
    };

    public Task<ProfileSettingsDto> GetProfileAsync(CancellationToken ct = default) =>
        GetAsync(SettingCategory.Profile, DefaultProfile, ct);

    public Task<ProfileSettingsDto> UpdateProfileAsync(ProfileSettingsDto request, CancellationToken ct = default) =>
        UpdateAsync(SettingCategory.Profile, DefaultProfile, request, "settings.profile.update", ct);

    private static readonly NotificationSettingsDto DefaultNotification = new()
    {
        MinimumSeverity = "Medium",
        RenotifyIntervalMinutes = 60,
        EmailEnabled = false,
        SmtpPort = 587,
        SmtpUseStartTls = true,
        PushEnabled = false,
        PushFailureThreshold = 3,
    };

    private static readonly BackupSettingsDto DefaultBackup = new()
    {
        Enabled = false,
        Prefix = "server-operations/",
        Region = "us-east-1",
        UsePathStyle = true,
        KeepGenerations = 7,
    };

    public Task<RetentionSettingsDto> GetRetentionAsync(CancellationToken ct = default) =>
        GetAsync(SettingCategory.Retention, DefaultRetention, ct);

    public Task<RetentionSettingsDto> UpdateRetentionAsync(RetentionSettingsDto request, CancellationToken ct = default) =>
        UpdateAsync(SettingCategory.Retention, DefaultRetention, request, "settings.retention.update", ct);

    public Task<NotificationSettingsDto> GetNotificationAsync(CancellationToken ct = default) =>
        GetAsync(SettingCategory.Notification, DefaultNotification, ct);

    public async Task<NotificationSettingsDto> UpdateNotificationAsync(
        NotificationSettingsDto request, CancellationToken ct = default)
    {
        await ValidateNotificationAsync(request, ct);

        return await UpdateAsync(
            SettingCategory.Notification, DefaultNotification, request,
            "settings.notification.update", ct);
    }

    public Task<BackupSettingsDto> GetBackupAsync(CancellationToken ct = default) =>
        GetAsync(SettingCategory.Backup, DefaultBackup, ct);

    public async Task<BackupSettingsDto> UpdateBackupAsync(
        BackupSettingsDto request, CancellationToken ct = default)
    {
        await ValidateBackupAsync(request, ct);

        return await UpdateAsync(
            SettingCategory.Backup, DefaultBackup, request, "settings.backup.update", ct);
    }

    public async Task<List<DTOs.Operations.NotificationTestResultDto>> SendTestNotificationAsync(
        CancellationToken ct = default)
    {
        var results = await notificationTest.SendTestAsync(ct);

        // 送信できたかどうかは設定の確認結果として残す。
        // 宛先・ホスト名・エラー本文は載せない(監査から接続先が読み取れないようにする)。
        var succeeded = results.Count(r => r.Success);
        var failed = results.Count(r => !r.Success && !r.Skipped);
        await audit.RecordAsync(
            "settings.notification.test", "Settings", "notification",
            failed == 0 ? AuditResult.Success : AuditResult.Failure,
            currentUser.UserId, currentUser.Username,
            $"sent={succeeded} failed={failed} skipped={results.Count(r => r.Skipped)}", ct);

        return results.Select(r => new DTOs.Operations.NotificationTestResultDto
        {
            Channel = r.Channel,
            Success = r.Success,
            Skipped = r.Skipped,
            Message = r.Message,
        }).ToList();
    }

    /// <summary>
    /// 通知設定の検証。
    /// 有効にしたのに送信先が無い、という状態で保存させない(通知が飛ばない原因になる)。
    /// </summary>
    private static async Task ValidateNotificationAsync(
        NotificationSettingsDto request, CancellationToken ct)
    {
        if (!request.EmailEnabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.SmtpHost))
        {
            throw AppException.BadRequest(
                "smtp_host_required", "メール通知を有効にする場合はSMTPサーバーを指定してください。");
        }

        if (string.IsNullOrWhiteSpace(request.SmtpFromAddress))
        {
            throw AppException.BadRequest(
                "smtp_from_required", "メール通知を有効にする場合は送信元アドレスを指定してください。");
        }

        var recipients = request.EmailRecipients
            .Select(r => r.Trim())
            .Where(r => r.Length > 0)
            .ToList();

        if (recipients.Count == 0)
        {
            throw AppException.BadRequest(
                "email_recipients_required", "メール通知を有効にする場合は送信先を1件以上指定してください。");
        }

        foreach (var address in recipients.Append(request.SmtpFromAddress.Trim()))
        {
            if (!IsLikelyEmailAddress(address))
            {
                throw AppException.BadRequest(
                    "invalid_email_address", $"メールアドレスの形式が正しくありません: {address}");
            }
        }

        // 任意のホスト・ポートへ接続させない。URLと同じ基準で確かめる。
        await EndpointValidator.ValidateHostPortAsync(request.SmtpHost, request.SmtpPort, ct);
    }

    private static async Task ValidateBackupAsync(BackupSettingsDto request, CancellationToken ct)
    {
        if (!request.Enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Endpoint))
        {
            throw AppException.BadRequest(
                "backup_endpoint_required", "バックアップを有効にする場合は保存先を指定してください。");
        }

        if (string.IsNullOrWhiteSpace(request.BucketName))
        {
            throw AppException.BadRequest(
                "backup_bucket_required", "バックアップを有効にする場合はバケット名を指定してください。");
        }

        // 保存先も任意URLを許さない。実行時にも同じ検証が入る。
        await EndpointValidator.ValidateHttpUrlAsync(request.Endpoint, ct);
    }

    /// <summary>
    /// メールアドレスの形をざっと確かめる。
    /// 厳密な検証はしない(実在確認はできないため)。明らかな誤りを弾くことが目的。
    /// </summary>
    private static bool IsLikelyEmailAddress(string value)
    {
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return false;
        }

        var domain = value[(at + 1)..];
        return domain.Contains('.') && !domain.StartsWith('.') && !domain.EndsWith('.')
            && !value.Any(char.IsWhiteSpace);
    }

    private async Task<T> GetAsync<T>(SettingCategory category, T defaultValue, CancellationToken ct)
    {
        var stored = await settings.FindByCategoryAsync(category, ct);
        if (stored is null)
        {
            return defaultValue;
        }

        return JsonSerializer.Deserialize<T>(stored.Value, JsonOptions) ?? defaultValue;
    }

    private async Task<T> UpdateAsync<T>(
        SettingCategory category, T defaultValue, T request, string auditAction, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stored = await settings.FindByCategoryAsync(category, ct);

        // 監査用の前後状態要約(設定カテゴリに秘密値は含まれない前提)
        var before = stored?.Value ?? JsonSerializer.Serialize(defaultValue, JsonOptions);
        var after = JsonSerializer.Serialize(request, JsonOptions);

        if (stored is null)
        {
            await settings.AddAsync(new SystemSetting
            {
                Category = category,
                Value = after,
                UpdatedAt = now,
                UpdatedByUserId = currentUser.UserId,
            }, ct);
        }
        else
        {
            stored.Value = after;
            stored.UpdatedAt = now;
            stored.UpdatedByUserId = currentUser.UserId;
        }

        await settings.SaveChangesAsync(ct);

        await audit.RecordAsync(
            auditAction, "SystemSetting", category.ToString(), AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"before={before} after={after}", ct: ct);

        return request;
    }
}
