using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
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
    IAdapterTemplateCatalog templateCatalog,
    IDataProtectionProvider dataProtectionProvider,
    IDiagnosisService diagnosisService,
    IResourceThresholdDetector resourceThresholdDetector,
    Notifications.INotificationService notificationService,
    IAutoRecoveryService autoRecoveryService,
    TimeProvider timeProvider,
    ILogger<TargetCollectionService> logger) : ITargetCollectionService
{
    /// <summary>
    /// 1回の収集でリソース使用率を測るコンテナ数の上限。
    /// 1件あたり約1秒かかるため、際限なく増やすと収集間隔を超える。
    /// </summary>
    public const int MaxStatsContainers = 20;

    /// <summary>リソース使用率の同時取得数。対象のDocker APIへ一度に集中させない。</summary>
    private const int StatsConcurrency = 4;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    // TargetServiceと同じ目的文字列(暗号化した資格情報を復号するため一致必須)
    private readonly IDataProtector _credentialProtector =
        dataProtectionProvider.CreateProtector("TargetCredential");

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

        var template = templateCatalog.Find(target.TemplateId);
        if (template is null)
        {
            logger.LogWarning(
                "Unknown template {TemplateId} for target {TargetId}", target.TemplateId, target.Id);
            return;
        }

        // この対象で行う収集だけに絞る。未設定ならテンプレートで行えるものすべて。
        var enabled = EnabledMonitors.Resolve(target, template);

        try
        {
            switch (target.TemplateId)
            {
                case "docker-host":
                    await CollectDockerAsync(
                        target.Id, settings["endpoint"], composeProject: null, enabled, ct);
                    break;
                case "docker-compose-app":
                    await CollectDockerAsync(
                        target.Id, settings["endpoint"],
                        settings.GetValueOrDefault("composeProject"), enabled, ct);
                    break;
                case "web-site":
                    if (enabled.Contains(MonitorKinds.HttpCheck, StringComparer.Ordinal))
                    {
                        await CollectHttpAsync(target, settings, ct);
                    }

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
        long targetId,
        string endpoint,
        string? composeProject,
        IReadOnlyList<string> enabledMonitors,
        CancellationToken ct)
    {
        var collectState = enabledMonitors.Contains(MonitorKinds.ContainerState, StringComparer.Ordinal);
        var collectResources = enabledMonitors.Contains(MonitorKinds.ResourceUsage, StringComparer.Ordinal);
        if (!collectState && !collectResources)
        {
            return;
        }

        var collectLogs = enabledMonitors.Contains(MonitorKinds.LogExcerpt, StringComparer.Ordinal);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // リソース使用率はコンテナごとの値のため、どのコンテナがあるかを知るのに一覧が要る。
        // 状態の収集を外していても、使用率を取るならこの呼び出しは避けられない。
        var containers = await dockerAdapter.ListContainersAsync(endpoint, composeProject, ct);

        if (collectState)
        {
            await CollectContainerStateAsync(
                targetId, endpoint, containers, collectLogs, now, ct);
        }

        // 使用率の取得は1件あたり約1秒かかる。
        // 停止コンテナの検出を待たせないよう、状態の収集を先に終わらせてから行う。
        if (collectResources)
        {
            await CollectResourceUsageAsync(targetId, endpoint, containers, ct);
        }
    }

    private async Task CollectContainerStateAsync(
        long targetId,
        string endpoint,
        IReadOnlyList<ContainerInfo> containers,
        bool collectLogs,
        DateTime now,
        CancellationToken ct)
    {
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
            // ログ抜粋を外してある対象では取りに行かない。
            // 一覧とは別のAPI呼び出しであり、外せば実際に呼ばなくなる。
            string logExcerpt = string.Empty;
            if (collectLogs)
            {
                try
                {
                    logExcerpt = await dockerAdapter.GetContainerLogsAsync(
                        endpoint, container.Id, 50, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    logExcerpt = string.Empty;
                }
            }

            var (incident, shouldDiagnose) = await UpsertIncidentAsync(
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

            if (shouldDiagnose)
            {
                var diagnosis = await diagnosisService.DiagnoseAsync(incident, new DiagnosticContext
                {
                    ContainerState = container.State,
                    ContainerName = container.Name,
                    RestartCount = container.RestartCount,
                    LogExcerpt = LogMasker.MaskSecrets(logExcerpt),
                }, ct);

                await TryAutoRecoverAsync(targetId, incident, diagnosis, ct);
            }
        }
    }

    /// <summary>
    /// 稼働中コンテナのCPU・メモリ使用率を収集し、しきい値ルールに当たったものをインシデント化する。
    /// 使用率が取れなかったコンテナは記録に残すが、正常とは扱わない。
    /// </summary>
    private async Task CollectResourceUsageAsync(
        long targetId, string endpoint, IReadOnlyList<ContainerInfo> containers, CancellationToken ct)
    {
        // 停止中のコンテナに使用率は無い。順序を固定するため名前で並べる
        var running = containers
            .Where(c => c.State.Equals("running", StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToList();

        var measured = running.Take(MaxStatsContainers).ToList();
        var skipped = running.Count - measured.Count;

        var stats = new ContainerStats?[measured.Count];
        using (var gate = new SemaphoreSlim(StatsConcurrency))
        {
            await Task.WhenAll(measured.Select(async (container, index) =>
            {
                await gate.WaitAsync(ct);
                try
                {
                    stats[index] = await dockerAdapter.GetContainerStatsAsync(endpoint, container.Id, ct);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    // 1件の取得失敗で対象全体の収集を落とさない
                    stats[index] = null;
                }
                finally
                {
                    gate.Release();
                }
            }));
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var samples = new List<ContainerResourceSample>();
        var payload = new List<object>(measured.Count);

        for (var i = 0; i < measured.Count; i++)
        {
            var name = measured[i].Name;
            var sample = stats[i];

            payload.Add(new
            {
                name,
                cpuUsagePercent = sample?.CpuUsagePercent,
                memoryUsagePercent = sample?.MemoryUsagePercent,
                memoryUsageBytes = sample?.MemoryUsageBytes,
                memoryLimitBytes = sample?.MemoryLimitBytes,
            });

            if (sample is not null)
            {
                samples.Add(new ContainerResourceSample(name, sample));
            }
        }

        // 測れたコンテナが1つも無い場合は失敗として残す。
        // 空の結果を正常な収集として記録すると、取得できていないことが見えなくなる
        var failed = measured.Count > 0 && samples.Count == 0;

        await snapshots.AddAsync(new MetricSnapshot
        {
            TargetId = targetId,
            CollectedAt = now,
            Kind = "resource",
            Status = failed ? CollectionStatus.Failed : CollectionStatus.Ok,
            PayloadJson = JsonSerializer.Serialize(new
            {
                measured = measured.Count,
                skipped,
                containers = payload,
            }, JsonOptions),
            ErrorMessage = failed ? "リソース使用率を取得できませんでした。" : null,
        }, ct);
        await snapshots.SaveChangesAsync(ct);

        if (skipped > 0)
        {
            logger.LogInformation(
                "Resource usage measured for {Measured} of {Total} running containers on target {TargetId}.",
                measured.Count, running.Count, targetId);
        }

        foreach (var alert in await resourceThresholdDetector.DetectAsync(samples, ct))
        {
            var (incident, shouldDiagnose) = await UpsertIncidentAsync(
                targetId,
                classification: alert.Rule.Classification,
                service: alert.ContainerName,
                title: $"コンテナ {alert.ContainerName}: {alert.Rule.Name}",
                severity: alert.Rule.Severity,
                logExcerpt: null,
                ct);

            if (shouldDiagnose)
            {
                // 診断も同じ文脈で行う。復旧はここでは試みない。
                // 使用率が高いだけでは「何を再起動すれば直るか」が定まらず、
                // 判断を人へ残すほうが安全である。
                await diagnosisService.DiagnoseAsync(incident, alert.Context, ct);
            }
        }
    }

    /// <summary>
    /// 診断結果に基づく自動復旧を試みる。実行条件の判定はAutoRecoveryServiceが行う。
    /// 失敗しても収集を止めない。
    /// </summary>
    private async Task TryAutoRecoverAsync(
        long targetId, Incident incident, Diagnosis? diagnosis, CancellationToken ct)
    {
        if (diagnosis is null)
        {
            return;
        }

        try
        {
            // 自動復旧の判定は最新の対象設定(自動復旧の有効/無効・許可コンテナ)で行う
            var target = await targets.FindByIdAsync(targetId, ct);
            if (target is null)
            {
                return;
            }

            await autoRecoveryService.TryRecoverAsync(target, incident, diagnosis, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Auto recovery failed for incident {IncidentId}.", incident.Id);
        }
    }

    private async Task CollectHttpAsync(
        Models.Operations.MonitoringTarget target, Dictionary<string, string> settings, CancellationToken ct)
    {
        var targetId = target.Id;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var url = settings["url"];
        var expectedStatus = int.TryParse(settings.GetValueOrDefault("expectedStatus"), out var es) ? es : 200;
        var timeoutSeconds = int.TryParse(settings.GetValueOrDefault("timeoutSeconds"), out var ts) ? ts : 10;

        // 設定済みのBasic認証を送信する(未送信だと保護されたエンドポイントで誤検知になる)
        var basicAuthPassword = target.Credentials
            .Where(c => c.Kind == "basicAuthPassword")
            .Select(c => _credentialProtector.Unprotect(c.ValueProtected))
            .FirstOrDefault();

        var result = await httpAdapter.TestConnectionAsync(new HttpCheckOptions
        {
            Url = url,
            ExpectedStatus = expectedStatus,
            TimeoutSeconds = timeoutSeconds,
            BasicAuthUser = settings.GetValueOrDefault("basicAuthUser"),
            BasicAuthPassword = basicAuthPassword,
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
            var (incident, shouldDiagnose) = await UpsertIncidentAsync(
                targetId,
                classification: "HttpUnavailable",
                service: null,
                title: "HTTPヘルスチェックが失敗しています",
                severity: IncidentSeverity.High,
                logExcerpt: result.Message,
                ct);

            if (shouldDiagnose)
            {
                var diagnosis = await diagnosisService.DiagnoseAsync(incident, new DiagnosticContext
                {
                    HttpSuccess = false,
                    HttpLatencyMs = result.LatencyMs,
                    LogExcerpt = result.Message,
                }, ct);

                await TryAutoRecoverAsync(targetId, incident, diagnosis, ct);
            }
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

        var (incident, shouldDiagnose) = await UpsertIncidentAsync(
            targetId,
            classification: "CollectionFailed",
            service: null,
            title: "監視データの収集に失敗しています",
            severity: IncidentSeverity.Medium,
            logExcerpt: null,
            ct);

        if (shouldDiagnose)
        {
            // 収集自体が失敗している状態では対象へ到達できないため、自動復旧は試みない
            await diagnosisService.DiagnoseAsync(incident, new DiagnosticContext(), ct);
        }
    }

    /// <summary>
    /// 障害署名で既存インシデントを検索し、あれば再発として関連付け(回数加算)、なければ新規作成する。
    /// Resolved状態で再発した場合はOpenへ戻す。
    /// </summary>
    private async Task<(Incident Incident, bool ShouldDiagnose)> UpsertIncidentAsync(
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

            // Resolvedからの再発時のみ再診断する(継続中の単純な再検知では診断を増やさない)
            var reopened = existing.Status == IncidentStatus.Resolved;
            if (reopened)
            {
                existing.Status = IncidentStatus.Open;
                existing.ResolvedAt = null;
            }

            await incidents.SaveChangesAsync(ct);
            return (existing, reopened);
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

        await NotifyIncidentAsync(incident, ct);
        return (incident, true);
    }

    /// <summary>
    /// インシデントを通知する。本文にはログ全文・秘密情報を含めず、要約のみを渡す。
    /// 同一障害署名は通知側で集約される。
    /// </summary>
    private async Task NotifyIncidentAsync(Incident incident, CancellationToken ct)
    {
        try
        {
            await notificationService.NotifyAsync(new Notifications.NotificationRequest
            {
                Severity = incident.Severity switch
                {
                    IncidentSeverity.Critical => Models.Operations.NotificationSeverity.Critical,
                    IncidentSeverity.High => Models.Operations.NotificationSeverity.High,
                    IncidentSeverity.Medium => Models.Operations.NotificationSeverity.Medium,
                    _ => Models.Operations.NotificationSeverity.Low,
                },
                Title = incident.Title,
                Body = $"分類: {incident.Classification}"
                    + (incident.Service is null ? string.Empty : $" / 対象: {incident.Service}"),
                AggregationKey = incident.SignatureSha256,
                IncidentId = incident.Id,
                TargetId = incident.TargetId,
            }, ct);
        }
        catch (Exception ex)
        {
            // 通知の失敗で収集を止めない
            logger.LogWarning(ex, "Failed to send notification for incident {IncidentId}.", incident.Id);
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
