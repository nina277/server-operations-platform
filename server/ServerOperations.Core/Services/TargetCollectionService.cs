using System.Text.Json;
using Microsoft.Extensions.Logging;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

public interface ITargetCollectionService
{
    /// <summary>指定対象の収集を1回実行する(メトリクス保存・障害検出・ログ抜粋保存)。</summary>
    Task CollectAsync(long targetId, CancellationToken ct = default);
}

/// <summary>
/// 対象別の収集処理。Docker状態・再起動回数・ログ抜粋・HTTP状態・応答時間を正規化して保存し、
/// 障害を検出した場合は障害署名で既存インシデントへ集約する。
/// </summary>
public class TargetCollectionService(
    IMonitoringTargetRepository targets,
    IMetricSnapshotRepository snapshots,
    IIncidentRepository incidents,
    IIncidentLogRepository incidentLogs,
    IDockerAdapter dockerAdapter,
    IHttpAdapter httpAdapter,
    TimeProvider timeProvider,
    ILogger<TargetCollectionService> logger) : ITargetCollectionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task CollectAsync(long targetId, CancellationToken ct = default)
    {
        var target = await targets.FindByIdAsync(targetId, ct);
        if (target is null || !target.IsEnabled)
        {
            return;
        }

        var settings = target.Profile is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(target.Profile.SettingsJson, JsonOptions) ?? [];

        try
        {
            switch (target.TemplateId)
            {
                case "docker-host":
                    await CollectDockerAsync(target.Id, settings["endpoint"], composeProject: null, ct);
                    break;
                case "docker-compose-app":
                    await CollectDockerAsync(target.Id, settings["endpoint"], settings.GetValueOrDefault("composeProject"), ct);
                    break;
                case "web-site":
                    await CollectHttpAsync(target.Id, settings, ct);
                    break;
                default:
                    logger.LogWarning("Unknown template {TemplateId} for target {TargetId}", target.TemplateId, target.Id);
                    return;
            }
        }
        catch (Exception ex)
        {
            // 収集失敗。古い値を正常値として扱わず、取得失敗として記録する
            logger.LogWarning(ex, "Collection failed for target {TargetId}", target.Id);
            await RecordCollectionFailureAsync(target.Id, target.TemplateId, ct);
        }
    }

    private async Task CollectDockerAsync(
        long targetId, string endpoint, string? composeProject, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var containers = await dockerAdapter.ListContainersAsync(endpoint, composeProject, ct);

        var payload = containers.Select(c => new
        {
            c.Name,
            c.Image,
            c.State,
            c.Status,
            c.RestartCount,
        }).ToList();

        await snapshots.AddAsync(new MetricSnapshot
        {
            TargetId = targetId,
            CollectedAt = now,
            Kind = "docker",
            Status = CollectionStatus.Ok,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
        }, ct);
        await snapshots.SaveChangesAsync(ct);

        // 停止コンテナを検出してインシデント化し、ログ抜粋を保存する
        foreach (var container in containers.Where(c =>
            c.State.Equals("exited", StringComparison.OrdinalIgnoreCase) ||
            c.State.Equals("dead", StringComparison.OrdinalIgnoreCase)))
        {
            string logExcerpt;
            try
            {
                logExcerpt = await dockerAdapter.GetContainerLogsAsync(endpoint, container.Id, 50, ct);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                logExcerpt = string.Empty;
            }

            var incident = await UpsertIncidentAsync(
                targetId,
                classification: "ContainerStopped",
                service: container.Name,
                title: $"コンテナ {container.Name} が停止しています",
                severity: IncidentSeverity.High,
                logExcerpt: logExcerpt,
                ct);

            if (!string.IsNullOrWhiteSpace(logExcerpt))
            {
                await incidentLogs.AddAsync(new IncidentLog
                {
                    TargetId = targetId,
                    IncidentId = incident.Id,
                    CollectedAt = now,
                    Source = container.Name,
                    MaskedContent = Truncate(LogMasker.MaskSecrets(logExcerpt), 16000),
                }, ct);
                await incidentLogs.SaveChangesAsync(ct);
            }
        }
    }

    private async Task CollectHttpAsync(
        long targetId, Dictionary<string, string> settings, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var url = settings["url"];
        var expectedStatus = int.TryParse(settings.GetValueOrDefault("expectedStatus"), out var es) ? es : 200;
        var timeoutSeconds = int.TryParse(settings.GetValueOrDefault("timeoutSeconds"), out var ts) ? ts : 10;

        // 収集はBasic認証なしの疎通確認とする(資格情報は接続試験でのみ使用)
        var result = await httpAdapter.TestConnectionAsync(new HttpCheckOptions
        {
            Url = url,
            ExpectedStatus = expectedStatus,
            TimeoutSeconds = timeoutSeconds,
        }, ct);

        await snapshots.AddAsync(new MetricSnapshot
        {
            TargetId = targetId,
            CollectedAt = now,
            Kind = "http",
            Status = result.Success ? CollectionStatus.Ok : CollectionStatus.Failed,
            PayloadJson = JsonSerializer.Serialize(new
            {
                success = result.Success,
                latencyMs = result.LatencyMs,
                message = result.Message,
            }, JsonOptions),
            ErrorMessage = result.Success ? null : result.Message,
        }, ct);
        await snapshots.SaveChangesAsync(ct);

        if (!result.Success)
        {
            await UpsertIncidentAsync(
                targetId,
                classification: "HttpUnavailable",
                service: null,
                title: "HTTPヘルスチェックが失敗しています",
                severity: IncidentSeverity.High,
                logExcerpt: result.Message,
                ct);
        }
    }

    private async Task RecordCollectionFailureAsync(long targetId, string templateId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        await snapshots.AddAsync(new MetricSnapshot
        {
            TargetId = targetId,
            CollectedAt = now,
            Kind = templateId.StartsWith("docker") ? "docker" : "http",
            Status = CollectionStatus.Failed,
            ErrorMessage = "収集に失敗しました(対象へ到達できません)。",
        }, ct);
        await snapshots.SaveChangesAsync(ct);

        await UpsertIncidentAsync(
            targetId,
            classification: "CollectionFailed",
            service: null,
            title: "監視データの収集に失敗しています",
            severity: IncidentSeverity.Medium,
            logExcerpt: null,
            ct);
    }

    /// <summary>
    /// 障害署名で既存インシデントを検索し、あれば再発として関連付け(回数加算)、なければ新規作成する。
    /// Resolved状態で再発した場合はOpenへ戻す。
    /// </summary>
    private async Task<Incident> UpsertIncidentAsync(
        long targetId,
        string classification,
        string? service,
        string title,
        IncidentSeverity severity,
        string? logExcerpt,
        CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var signature = FailureSignature.Compute(targetId, service, classification, logExcerpt);

        var existing = await incidents.FindOpenBySignatureAsync(targetId, signature, ct);
        if (existing is not null)
        {
            existing.OccurrenceCount++;
            existing.LastOccurredAt = now;
            existing.UpdatedAt = now;
            if (existing.Status == IncidentStatus.Resolved)
            {
                existing.Status = IncidentStatus.Open;
                existing.ResolvedAt = null;
            }

            await incidents.SaveChangesAsync(ct);
            return existing;
        }

        var incident = new Incident
        {
            TargetId = targetId,
            SignatureSha256 = signature,
            Title = title,
            Classification = classification,
            Service = service,
            Severity = severity,
            Status = IncidentStatus.Open,
            FirstOccurredAt = now,
            LastOccurredAt = now,
            OccurrenceCount = 1,
            CreatedAt = now,
            UpdatedAt = now,
        };
        await incidents.AddAsync(incident, ct);
        await incidents.SaveChangesAsync(ct);
        return incident;
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
