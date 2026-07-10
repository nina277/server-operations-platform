using ServerOperations.Api.DTOs.Settings;

namespace ServerOperations.Api.Services.Interfaces;

public interface ISettingsService
{
    Task<ProfileSettingsDto> GetProfileAsync(CancellationToken ct = default);

    Task<ProfileSettingsDto> UpdateProfileAsync(ProfileSettingsDto request, CancellationToken ct = default);

    Task<RetentionSettingsDto> GetRetentionAsync(CancellationToken ct = default);

    Task<RetentionSettingsDto> UpdateRetentionAsync(RetentionSettingsDto request, CancellationToken ct = default);
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
