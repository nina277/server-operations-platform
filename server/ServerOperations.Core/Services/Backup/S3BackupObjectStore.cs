using Amazon.S3;
using Amazon.S3.Model;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Models.Settings;

namespace ServerOperations.Core.Services.Backup;

/// <summary>
/// S3互換の保存先からバックアップを読む。
///
/// 復元の判断はここに置かない(<see cref="BackupRestoreService"/> 側にある)。
/// ここは取ってくるだけ。
/// </summary>
public class S3BackupObjectStore(IBackupSettingsProvider settingsProvider) : IBackupObjectStore
{
    public async Task<List<BackupGeneration>> ListAsync(CancellationToken ct = default)
    {
        var settings = await settingsProvider.GetAsync(ct);
        using var client = await CreateClientAsync(settings, ct);

        var listed = await client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = settings.BucketName,
            Prefix = settings.Prefix,
        }, ct);

        return (listed.S3Objects ?? [])
            .OrderByDescending(o => o.LastModified)
            .Select(o => new BackupGeneration(o.Key, o.LastModified ?? default, o.Size ?? 0))
            .ToList();
    }

    public async Task<byte[]> GetAsync(string objectKey, CancellationToken ct = default)
    {
        var settings = await settingsProvider.GetAsync(ct);
        using var client = await CreateClientAsync(settings, ct);
        using var response = await client.GetObjectAsync(settings.BucketName, objectKey, ct);
        using var buffer = new MemoryStream();
        await response.ResponseStream.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    private async Task<IAmazonS3> CreateClientAsync(BackupSettings settings, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint)
            || string.IsNullOrWhiteSpace(settings.BucketName))
        {
            throw new InvalidOperationException("保存先が設定されていません。");
        }

        // 保存先のURLも収集先と同じ基準で確かめる(内部アドレスへ向けさせない)
        await EndpointValidator.ValidateHttpUrlAsync(settings.Endpoint, ct);

        return new AmazonS3Client(
            await settingsProvider.GetAccessKeyAsync(ct),
            await settingsProvider.GetSecretKeyAsync(ct),
            new AmazonS3Config
            {
                ServiceURL = settings.Endpoint,
                ForcePathStyle = settings.UsePathStyle,
                AuthenticationRegion = settings.Region,
                Timeout = TimeSpan.FromMinutes(5),
            });
    }
}
