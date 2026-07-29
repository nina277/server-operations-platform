using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services.Notifications;

namespace ServerOperations.Worker;

/// <summary>
/// Worker側の通知設定・秘密値の取得。APIと同じ鍵リング・目的文字列を使う。
/// </summary>
public class WorkerNotificationSettingsProvider(
    ISystemSettingRepository settings,
    IEncryptedSecretRepository secrets,
    IDataProtectionProvider dataProtectionProvider) : INotificationSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("EncryptedSecret");

    public async Task<NotificationSettings> GetAsync(CancellationToken ct = default)
    {
        var stored = await settings.FindByCategoryAsync(SettingCategory.Notification, ct);
        return stored is null
            ? new NotificationSettings()
            : JsonSerializer.Deserialize<NotificationSettings>(stored.Value, JsonOptions) ?? new NotificationSettings();
    }

    public Task<string?> GetSmtpPasswordAsync(CancellationToken ct = default) =>
        UnprotectAsync("smtp-password", ct);

    public Task<string?> GetFcmServiceAccountJsonAsync(CancellationToken ct = default) =>
        UnprotectAsync("fcm-service-account", ct);

    private async Task<string?> UnprotectAsync(string kind, CancellationToken ct)
    {
        var secret = await secrets.FindByKindAsync(kind, ct);
        return secret is null ? null : _protector.Unprotect(secret.ValueProtected);
    }
}
