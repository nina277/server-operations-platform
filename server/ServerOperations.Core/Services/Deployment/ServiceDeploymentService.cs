using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Services.Deployment;

/// <summary>展開の下見または結果。</summary>
public record DeploymentPlan(
    string ServiceName,
    string ContainerName,
    string Image,
    Dictionary<int, int> Ports,
    List<string> Volumes,
    List<string> EnvironmentKeys,
    List<string> Warnings,
    bool Applied);

public interface IServiceDeploymentService
{
    /// <summary>展開の下見。**何も作らない。**</summary>
    Task<DeploymentPlan> PreviewAsync(
        long targetId, long templateId, string serviceName,
        Dictionary<string, string> inputs, CancellationToken ct = default);

    /// <summary>展開する。</summary>
    Task<DeployedService> DeployAsync(
        long targetId, long templateId, string serviceName,
        Dictionary<string, string> inputs, long? userId, CancellationToken ct = default);

    /// <summary>展開したサービスを削除する。**稼働中は削除しない。**</summary>
    Task<DeployedService> RemoveAsync(long deployedServiceId, long? userId, CancellationToken ct = default);
}

/// <summary>
/// テンプレートからサービスを展開する。
///
/// **第2層(人が明示的に起動する運用操作)。**
/// 診断・AI・ルールからは到達できない(ActionTierBoundaryTests が保証する)。
///
/// 展開先へは <see cref="IDeploymentAdapter"/> 経由で接続する。
/// これは監視用とは別の、権限の広い経路を使う。
/// </summary>
public partial class ServiceDeploymentService(
    AppDbContext db,
    IDeploymentAdapter deploymentAdapter,
    TimeProvider timeProvider,
    ILogger<ServiceDeploymentService> logger) : IServiceDeploymentService
{
    [GeneratedRegex(@"^[a-z0-9][a-z0-9_.-]{1,62}$")]
    private static partial Regex NamePattern();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // ホスト側で使わせないポート。SSHやDNSを塞ぐと復旧手段まで失う
    private static readonly int[] ReservedPorts = [22, 53, 3306, 8080];

    public async Task<DeploymentPlan> PreviewAsync(
        long targetId, long templateId, string serviceName,
        Dictionary<string, string> inputs, CancellationToken ct = default) =>
        (await BuildAsync(targetId, templateId, serviceName, inputs, ct)).Plan;

    public async Task<DeployedService> DeployAsync(
        long targetId, long templateId, string serviceName,
        Dictionary<string, string> inputs, long? userId, CancellationToken ct = default)
    {
        var (plan, spec, template, endpoint) =
            await BuildAsync(targetId, templateId, serviceName, inputs, ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var record = new DeployedService
        {
            Name = serviceName,
            TargetId = targetId,
            TemplateId = template.Id,
            ContainerName = spec.Name,
            Image = spec.Image,
            Status = DeployedServiceStatus.Deploying,
            // **秘密値は記録しない。**
            InputsJson = JsonSerializer.Serialize(
                inputs.Where(i => !IsSecret(template, i.Key)).ToDictionary(i => i.Key, i => i.Value),
                JsonOptions),
            DeployedAt = now,
            UpdatedAt = now,
            DeployedByUserId = userId,
        };
        db.DeployedServices.Add(record);
        await db.SaveChangesAsync(ct);

        try
        {
            // 1. イメージを取得する
            var pull = await deploymentAdapter.PullImageAsync(endpoint, spec.Image, ct);
            if (!pull.Success)
            {
                return await FailAsync(record, pull.Message, ct);
            }

            // 2. ボリュームを用意する(既にあれば何もしない)
            foreach (var volume in spec.Volumes.Keys)
            {
                var result = await deploymentAdapter.EnsureVolumeAsync(endpoint, volume, ct);
                if (!result.Success)
                {
                    return await FailAsync(record, result.Message, ct);
                }
            }

            // 3. コンテナを作る(同名があれば失敗する。黙って置き換えない)
            var created = await deploymentAdapter.CreateContainerAsync(endpoint, spec, ct);
            if (!created.Success)
            {
                return await FailAsync(record, created.Message, ct);
            }

            record.Status = DeployedServiceStatus.Stopped;
            record.Message = "展開しました。起動は別の操作で行います。";
            record.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "Service {Name} deployed as {Container} by user {UserId}.",
                serviceName, spec.Name, userId);
            return record;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Deployment failed for {Name}.", serviceName);
            return await FailAsync(record, "展開の途中でエラーが発生しました。", ct);
        }
    }

    public async Task<DeployedService> RemoveAsync(
        long deployedServiceId, long? userId, CancellationToken ct = default)
    {
        var record = await db.DeployedServices
            .Include(s => s.Target).ThenInclude(t => t!.Profile)
            .FirstOrDefaultAsync(s => s.Id == deployedServiceId, ct)
            ?? throw new InvalidOperationException("対象の展開記録がありません。");

        var endpoint = DeployEndpoint(record.Target);
        var result = await deploymentAdapter.RemoveContainerAsync(endpoint, record.ContainerName, ct);
        if (!result.Success)
        {
            // 稼働中の削除はここで止まる。**勝手に停止させない**
            record.Message = result.Message;
            record.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);
            throw new InvalidOperationException(result.Message);
        }

        record.Status = DeployedServiceStatus.Removed;
        record.Message = "削除しました。";
        record.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        logger.LogWarning(
            "Service {Name} removed by user {UserId}.", record.Name, userId);
        return record;
    }

    // --- 組み立てと検証 -------------------------------------------------

    private async Task<(DeploymentPlan Plan, ContainerSpec Spec, ServiceTemplate Template, string Endpoint)>
        BuildAsync(
            long targetId, long templateId, string serviceName,
            Dictionary<string, string> inputs, CancellationToken ct)
    {
        if (!NamePattern().IsMatch(serviceName))
        {
            throw new InvalidOperationException(
                "サービス名は英小文字・数字・ハイフン・アンダースコアで2〜63文字にしてください。");
        }

        var target = await db.MonitoringTargets.Include(t => t.Profile)
            .FirstOrDefaultAsync(t => t.Id == targetId, ct)
            ?? throw new InvalidOperationException("展開先の対象がありません。");

        var template = await db.ServiceTemplates.Include(t => t.Inputs)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct)
            ?? throw new InvalidOperationException("テンプレートがありません。");

        ValidateImage(template.Image);

        var containerName = $"svc-{serviceName}";
        var warnings = new List<string>();
        var env = new Dictionary<string, string>();
        var ports = new Dictionary<int, int>();
        var volumes = new Dictionary<string, string>();

        foreach (var input in template.Inputs.OrderBy(i => i.SortOrder))
        {
            var raw = inputs.GetValueOrDefault(input.Key) ?? input.DefaultValue;

            if (string.IsNullOrWhiteSpace(raw))
            {
                if (input.Required)
                {
                    throw new InvalidOperationException($"{input.Label} は必須です。");
                }

                continue;
            }

            switch (input.Type)
            {
                case ServiceInputType.Port:
                    if (!int.TryParse(raw, out var hostPort) || hostPort is < 1024 or > 65535)
                    {
                        throw new InvalidOperationException(
                            $"{input.Label} は1024〜65535のポート番号にしてください。");
                    }

                    // **監視システム自身や、復旧手段のポートを塞がせない**
                    if (ReservedPorts.Contains(hostPort))
                    {
                        throw new InvalidOperationException(
                            $"ポート {hostPort} は使えません(このシステムやSSHが使用します)。");
                    }

                    ports[hostPort] = input.ContainerPort ?? hostPort;
                    break;

                case ServiceInputType.Volume:
                    if (string.IsNullOrWhiteSpace(input.ContainerPath))
                    {
                        throw new InvalidOperationException(
                            $"{input.Label} のマウント先が定義されていません。");
                    }

                    // **名前付きボリュームだけ。**ホストのパスをマウントさせない
                    volumes[$"{containerName}-{input.Key}"] = input.ContainerPath;
                    break;

                case ServiceInputType.Secret:
                    env[input.Key] = raw;
                    break;

                default:
                    env[input.Key] = raw;
                    break;
            }
        }

        if (ports.Count == 0)
        {
            warnings.Add("公開ポートがありません。LAN内から到達できない構成になります。");
        }

        var spec = new ContainerSpec
        {
            Name = containerName,
            Image = template.Image,
            Environment = env,
            Ports = ports,
            Volumes = volumes,
            MemoryLimitBytes = (long)template.MemoryLimitMb * 1024 * 1024,
            Labels = new Dictionary<string, string>
            {
                ["managed-by"] = "server-operations-platform",
                ["service-name"] = serviceName,
                ["template"] = template.Key,
            },
        };

        var plan = new DeploymentPlan(
            serviceName, containerName, template.Image, ports,
            volumes.Keys.ToList(),
            // **秘密値の中身は下見にも出さない。**項目名だけ示す
            env.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
            warnings, Applied: false);

        return (plan, spec, template, DeployEndpoint(target));
    }

    /// <summary>展開先の接続先。監視用とは別の設定値から引く。</summary>
    private static string DeployEndpoint(MonitoringTarget? target)
    {
        var settings = target?.Profile is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(
                target.Profile.SettingsJson, JsonOptions) ?? [];

        // **監視用の endpoint は使わない。**権限が違う経路を混同しないため、
        // 展開先は別のキーで明示的に設定させる
        return settings.GetValueOrDefault("deployEndpoint")
            ?? throw new InvalidOperationException(
                "この対象には展開先(deployEndpoint)が設定されていません。"
                + "監視用の接続先とは権限が異なるため、別に指定する必要があります。");
    }

    internal static void ValidateImage(string image)
    {
        if (!image.Contains(':') || image.EndsWith(":latest", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "イメージは版を指定してください(latest は使えません)。"
                + "同じテンプレートから展開しても別のものが動く可能性があるためです。");
        }
    }

    private static bool IsSecret(ServiceTemplate template, string key) =>
        template.Inputs.Any(i => i.Key == key && i.Type == ServiceInputType.Secret);

    private async Task<DeployedService> FailAsync(
        DeployedService record, string message, CancellationToken ct)
    {
        record.Status = DeployedServiceStatus.Failed;
        record.Message = message;
        record.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return record;
    }
}
