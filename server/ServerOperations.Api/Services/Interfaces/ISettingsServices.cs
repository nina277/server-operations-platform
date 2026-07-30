using ServerOperations.Api.DTOs.Settings;

namespace ServerOperations.Api.Services.Interfaces;

public interface ISettingsService
{
    Task<ProfileSettingsDto> GetProfileAsync(CancellationToken ct = default);

    Task<ProfileSettingsDto> UpdateProfileAsync(ProfileSettingsDto request, CancellationToken ct = default);

    Task<RetentionSettingsDto> GetRetentionAsync(CancellationToken ct = default);

    Task<RetentionSettingsDto> UpdateRetentionAsync(RetentionSettingsDto request, CancellationToken ct = default);

    Task<NotificationSettingsDto> GetNotificationAsync(CancellationToken ct = default);

    /// <summary>
    /// 通知設定を保存する。
    /// メールを有効にする場合は送信先とSMTPの指定を必須とし、接続先の妥当性も確かめる。
    /// </summary>
    Task<NotificationSettingsDto> UpdateNotificationAsync(
        NotificationSettingsDto request, CancellationToken ct = default);

    Task<BackupSettingsDto> GetBackupAsync(CancellationToken ct = default);

    /// <summary>
    /// バックアップ設定を保存する。
    /// 有効にする場合は保存先とバケットの指定を必須とし、接続先の妥当性も確かめる。
    /// </summary>
    Task<BackupSettingsDto> UpdateBackupAsync(
        BackupSettingsDto request, CancellationToken ct = default);
}

public interface ISecretsService
{
    /// <summary>許可されている秘密値の種別一覧。</summary>
    IReadOnlyList<string> AllowedKinds { get; }

    Task<SecretStatusDto> GetStatusAsync(string kind, CancellationToken ct = default);

    Task<SecretStatusDto> UpdateAsync(string kind, string value, CancellationToken ct = default);
}

public interface INetworkCidrService
{
    Task<List<NetworkCidrDto>> GetAllAsync(CancellationToken ct = default);

    Task<NetworkCidrDto> AddAsync(CreateNetworkCidrRequest request, CancellationToken ct = default);

    Task DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>指定IPが許可範囲内か判定する。CIDRが未登録の場合はtrue(初期セットアップ用)。</summary>
    Task<bool> IsAllowedAsync(System.Net.IPAddress? remoteIp, CancellationToken ct = default);
}
