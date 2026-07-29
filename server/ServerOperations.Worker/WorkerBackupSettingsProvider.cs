using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services.Backup;

namespace ServerOperations.Worker;

/// <summary>Worker側のバックアップ設定・秘密値の取得。APIと同じ鍵リング・目的文字列を使う。</summary>
public class WorkerBackupSettingsProvider(
    ISystemSettingRepository settings,
    IEncryptedSecretRepository secrets,
    IDataProtectionProvider dataProtectionProvider) : IBackupSettingsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("EncryptedSecret");

    public async Task<BackupSettings> GetAsync(CancellationToken ct = default)
    {
        var stored = await settings.FindByCategoryAsync(SettingCategory.Backup, ct);
        return stored is null
            ? new BackupSettings()
            : JsonSerializer.Deserialize<BackupSettings>(stored.Value, JsonOptions) ?? new BackupSettings();
    }

    public Task<string?> GetAccessKeyAsync(CancellationToken ct = default) => UnprotectAsync("backup-access-key", ct);

    public Task<string?> GetSecretKeyAsync(CancellationToken ct = default) => UnprotectAsync("backup-secret-key", ct);

    public Task<string?> GetEncryptionKeyAsync(CancellationToken ct = default) =>
        UnprotectAsync("backup-encryption-key", ct);

    private async Task<string?> UnprotectAsync(string kind, CancellationToken ct)
    {
        var secret = await secrets.FindByKindAsync(kind, ct);
        return secret is null ? null : _protector.Unprotect(secret.ValueProtected);
    }
}
