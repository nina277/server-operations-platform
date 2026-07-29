using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services.Notifications;

namespace ServerOperations.Api.Services.Implementations;

/// <summary>
/// 通知設定と秘密値の取得。秘密値は都度復号し、保持・ログ出力しない。
/// </summary>
public class NotificationSettingsProvider(
    ISystemSettingRepository settings,
    IEncryptedSecretRepository secrets,
    IDataProtectionProvider dataProtectionProvider) : INotificationSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // SecretsServiceと同じ目的文字列(復号のため一致必須)
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("EncryptedSecret");

    public async Task<NotificationSettings> GetAsync(CancellationToken ct = default)
    {
        var stored = await settings.FindByCategoryAsync(SettingCategory.Notification, ct);
        if (stored is null)
        {
            return new NotificationSettings();
        }

        return JsonSerializer.Deserialize<NotificationSettings>(stored.Value, JsonOptions)
            ?? new NotificationSettings();
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
