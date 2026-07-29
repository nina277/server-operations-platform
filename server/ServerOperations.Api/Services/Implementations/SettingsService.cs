using System.Text.Json;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class SettingsService(
    ISystemSettingRepository settings,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
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

    public Task<RetentionSettingsDto> GetRetentionAsync(CancellationToken ct = default) =>
        GetAsync(SettingCategory.Retention, DefaultRetention, ct);

    public Task<RetentionSettingsDto> UpdateRetentionAsync(RetentionSettingsDto request, CancellationToken ct = default) =>
        UpdateAsync(SettingCategory.Retention, DefaultRetention, request, "settings.retention.update", ct);

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
