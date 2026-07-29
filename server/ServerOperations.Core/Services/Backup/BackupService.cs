using System.Security.Cryptography;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Models.Settings;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services.Notifications;

namespace ServerOperations.Core.Services.Backup;

public interface IBackupSettingsProvider
{
    Task<BackupSettings> GetAsync(CancellationToken ct = default);

    Task<string?> GetAccessKeyAsync(CancellationToken ct = default);

    Task<string?> GetSecretKeyAsync(CancellationToken ct = default);

    /// <summary>バックアップ暗号化キー(未設定ならnull)。</summary>
    Task<string?> GetEncryptionKeyAsync(CancellationToken ct = default);
}

public interface IBackupService
{
    /// <summary>保存先への接続試験。資格情報は結果へ含めない。</summary>
    Task<AdapterConnectionResult> TestConnectionAsync(CancellationToken ct = default);

    /// <summary>暗号化バックアップを実行する。失敗はHigh通知する。</summary>
    Task<BackupRun> RunAsync(long? triggeredByUserId = null, CancellationToken ct = default);
}

/// <summary>
/// S3互換ストレージ(MinIO / NAS)への暗号化バックアップ。
/// アップロード前にAES-256-GCMで暗号化し、平文を保存先へ送らない。
/// </summary>
public class BackupService(
    IBackupSettingsProvider settingsProvider,
    IBackupRunRepository runs,
    IBackupSourceProvider sourceProvider,
    INotificationService notifications,
    TimeProvider timeProvider,
    ILogger<BackupService> logger) : IBackupService
{
    public async Task<AdapterConnectionResult> TestConnectionAsync(CancellationToken ct = default)
    {
        var settings = await settingsProvider.GetAsync(ct);

        var validation = ValidateSettings(settings);
        if (validation is not null)
        {
            return new AdapterConnectionResult(false, validation);
        }

        try
        {
            await EndpointValidator.ValidateHttpUrlAsync(settings.Endpoint!, ct);
        }
        catch (AppException ex)
        {
            return new AdapterConnectionResult(false, ex.Message);
        }

        try
        {
            using var client = await CreateClientAsync(settings, ct);
            var response = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = settings.BucketName,
                Prefix = settings.Prefix,
                MaxKeys = 1,
            }, ct);

            return new AdapterConnectionResult(
                true, $"バケット {settings.BucketName} へ接続できました。", null,
                $"既存オブジェクト数(先頭のみ確認): {response.KeyCount}");
        }
        catch (AmazonS3Exception ex)
        {
            logger.LogWarning(ex, "Backup connection test failed.");
            return new AdapterConnectionResult(
                false, $"保存先へ接続できません(HTTP {(int)ex.StatusCode})。設定を確認してください。");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Backup connection test failed.");
            return new AdapterConnectionResult(false, "保存先へ接続できません(到達不能またはタイムアウト)。");
        }
    }

    public async Task<BackupRun> RunAsync(long? triggeredByUserId = null, CancellationToken ct = default)
    {
        var startedAt = timeProvider.GetUtcNow().UtcDateTime;
        var run = new BackupRun
        {
            Status = BackupStatus.Running,
            StartedAt = startedAt,
            TriggeredByUserId = triggeredByUserId,
        };
        await runs.AddAsync(run, ct);
        await runs.SaveChangesAsync(ct);

        try
        {
            var settings = await settingsProvider.GetAsync(ct);

            var validation = ValidateSettings(settings);
            if (validation is not null)
            {
                return await FailAsync(run, validation, ct);
            }

            if (!settings.Enabled)
            {
                return await FailAsync(run, "バックアップが無効です。", ct);
            }

            var encryptionKey = await settingsProvider.GetEncryptionKeyAsync(ct);
            if (string.IsNullOrWhiteSpace(encryptionKey))
            {
                return await FailAsync(run, "バックアップ暗号化キーが設定されていません。", ct);
            }

            await EndpointValidator.ValidateHttpUrlAsync(settings.Endpoint!, ct);

            var plaintext = await sourceProvider.CreateSnapshotAsync(ct);
            var encrypted = Encrypt(plaintext, encryptionKey);

            var objectKey = $"{settings.Prefix}backup-{startedAt:yyyyMMdd-HHmmss}.bin";

            using var client = await CreateClientAsync(settings, ct);
            using var stream = new MemoryStream(encrypted);
            await client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = settings.BucketName,
                Key = objectKey,
                InputStream = stream,
                ContentType = "application/octet-stream",
            }, ct);

            run.Status = BackupStatus.Succeeded;
            run.ObjectKey = objectKey;
            run.SizeBytes = encrypted.Length;
            run.Message = "バックアップが完了しました。";
            run.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
            await runs.SaveChangesAsync(ct);

            await PruneOldGenerationsAsync(client, settings, ct);
            return run;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Backup failed.");
            return await FailAsync(run, "バックアップ処理中にエラーが発生しました。", ct);
        }
    }

    /// <summary>失敗を記録し、High通知を送る。</summary>
    private async Task<BackupRun> FailAsync(BackupRun run, string message, CancellationToken ct)
    {
        run.Status = BackupStatus.Failed;
        run.Message = message;
        run.CompletedAt = timeProvider.GetUtcNow().UtcDateTime;
        await runs.SaveChangesAsync(ct);

        try
        {
            await notifications.NotifyAsync(new NotificationRequest
            {
                Severity = NotificationSeverity.High,
                Title = "バックアップに失敗しました",
                Body = message,
                AggregationKey = "backup-failed",
            }, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send backup failure notification.");
        }

        return run;
    }

    /// <summary>保持世代数を超えた古いバックアップを削除する。</summary>
    private async Task PruneOldGenerationsAsync(
        IAmazonS3 client, BackupSettings settings, CancellationToken ct)
    {
        if (settings.KeepGenerations <= 0)
        {
            return;
        }

        try
        {
            var listed = await client.ListObjectsV2Async(new ListObjectsV2Request
            {
                BucketName = settings.BucketName,
                Prefix = settings.Prefix,
            }, ct);

            var obsolete = (listed.S3Objects ?? [])
                .OrderByDescending(o => o.LastModified)
                .Skip(settings.KeepGenerations)
                .ToList();

            foreach (var item in obsolete)
            {
                await client.DeleteObjectAsync(settings.BucketName, item.Key, ct);
            }

            if (obsolete.Count > 0)
            {
                logger.LogInformation("Pruned {Count} old backup objects.", obsolete.Count);
            }
        }
        catch (Exception ex) when (ex is AmazonS3Exception or HttpRequestException or TaskCanceledException)
        {
            // 世代削除の失敗はバックアップ自体の成否に影響させない
            logger.LogWarning(ex, "Failed to prune old backup generations.");
        }
    }

    private static string? ValidateSettings(BackupSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            return "保存先エンドポイントが設定されていません。";
        }

        if (string.IsNullOrWhiteSpace(settings.BucketName))
        {
            return "バケット名が設定されていません。";
        }

        return null;
    }

    private async Task<IAmazonS3> CreateClientAsync(BackupSettings settings, CancellationToken ct)
    {
        var accessKey = await settingsProvider.GetAccessKeyAsync(ct);
        var secretKey = await settingsProvider.GetSecretKeyAsync(ct);

        return new AmazonS3Client(accessKey, secretKey, new AmazonS3Config
        {
            ServiceURL = settings.Endpoint,
            ForcePathStyle = settings.UsePathStyle,
            AuthenticationRegion = settings.Region,
            Timeout = TimeSpan.FromMinutes(5),
        });
    }

    /// <summary>AES-256-GCMで暗号化する。出力は nonce(12) + tag(16) + 暗号文。</summary>
    internal static byte[] Encrypt(byte[] plaintext, string key)
    {
        // 任意長のキー文字列からSHA-256で256bitキーを導出する
        var derivedKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));

        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var ciphertext = new byte[plaintext.Length];

        using var aes = new AesGcm(derivedKey, AesGcm.TagByteSizes.MaxSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        var output = new byte[nonce.Length + tag.Length + ciphertext.Length];
        nonce.CopyTo(output, 0);
        tag.CopyTo(output, nonce.Length);
        ciphertext.CopyTo(output, nonce.Length + tag.Length);
        return output;
    }

    /// <summary>復元用。Encryptの出力形式を前提とする。</summary>
    internal static byte[] Decrypt(byte[] encrypted, string key)
    {
        var derivedKey = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(key));
        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;

        if (encrypted.Length < nonceSize + tagSize)
        {
            throw new CryptographicException("暗号化データの形式が正しくありません。");
        }

        var nonce = encrypted.AsSpan(0, nonceSize);
        var tag = encrypted.AsSpan(nonceSize, tagSize);
        var ciphertext = encrypted.AsSpan(nonceSize + tagSize);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(derivedKey, tagSize);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return plaintext;
    }
}
