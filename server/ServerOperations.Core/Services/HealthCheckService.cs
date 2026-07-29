using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

/// <summary>
/// 対象のヘルスチェック。復旧後の確認と、利用者からの手動実行の両方で使う。
/// </summary>
public class HealthCheckService(
    IMonitoringTargetRepository targets,
    IHealthCheckRepository healthChecks,
    IDockerAdapter dockerAdapter,
    IHttpAdapter httpAdapter,
    IDataProtectionProvider dataProtectionProvider,
    TimeProvider timeProvider) : IHealthCheckService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _credentialProtector =
        dataProtectionProvider.CreateProtector("TargetCredential");

    public async Task<HealthCheck> RunAsync(
        long targetId, long? recoveryActionId = null, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var target = await targets.FindByIdAsync(targetId, ct);

        if (target is null)
        {
            return await SaveAsync(new HealthCheck
            {
                TargetId = targetId,
                RecoveryActionId = recoveryActionId,
                Status = HealthCheckResultStatus.Error,
                Message = "対象が見つかりません。",
                CheckedAt = now,
            }, ct);
        }

        var settings = target.Profile is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(target.Profile.SettingsJson, JsonOptions) ?? [];

        try
        {
            AdapterConnectionResult result;
            switch (target.TemplateId)
            {
                case AdapterTemplateCatalog.WebSite:
                {
                    var url = settings["url"];
                    await EndpointValidator.ValidateHttpUrlAsync(url, ct);
                    var basicAuthPassword = target.Credentials
                        .Where(c => c.Kind == "basicAuthPassword")
                        .Select(c => _credentialProtector.Unprotect(c.ValueProtected))
                        .FirstOrDefault();

                    result = await httpAdapter.TestConnectionAsync(new HttpCheckOptions
                    {
                        Url = url,
                        ExpectedStatus = int.TryParse(settings.GetValueOrDefault("expectedStatus"), out var es) ? es : 200,
                        TimeoutSeconds = int.TryParse(settings.GetValueOrDefault("timeoutSeconds"), out var ts) ? ts : 10,
                        BasicAuthUser = settings.GetValueOrDefault("basicAuthUser"),
                        BasicAuthPassword = basicAuthPassword,
                    }, ct);
                    break;
                }

                case AdapterTemplateCatalog.DockerHost:
                case AdapterTemplateCatalog.DockerComposeApp:
                {
                    var endpoint = settings["endpoint"];
                    await EndpointValidator.ValidateDockerEndpointAsync(endpoint, ct);
                    var containers = await dockerAdapter.ListContainersAsync(
                        endpoint, settings.GetValueOrDefault("composeProject"), ct);

                    var stopped = containers
                        .Where(c => !c.State.Equals("running", StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.Name)
                        .ToList();

                    result = stopped.Count == 0
                        ? new AdapterConnectionResult(true, $"全{containers.Count}コンテナが稼働しています。")
                        : new AdapterConnectionResult(
                            false, $"稼働していないコンテナ: {string.Join(", ", stopped)}");
                    break;
                }

                default:
                    result = new AdapterConnectionResult(false, "このテンプレートのヘルスチェックは未対応です。");
                    break;
            }

            return await SaveAsync(new HealthCheck
            {
                TargetId = targetId,
                RecoveryActionId = recoveryActionId,
                Status = result.Success ? HealthCheckResultStatus.Healthy : HealthCheckResultStatus.Unhealthy,
                Message = result.Message,
                LatencyMs = result.LatencyMs,
                CheckedAt = now,
            }, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or KeyNotFoundException)
        {
            return await SaveAsync(new HealthCheck
            {
                TargetId = targetId,
                RecoveryActionId = recoveryActionId,
                Status = HealthCheckResultStatus.Error,
                Message = "ヘルスチェックを実行できませんでした(対象へ到達できないか設定が不足しています)。",
                CheckedAt = now,
            }, ct);
        }
    }

    private async Task<HealthCheck> SaveAsync(HealthCheck check, CancellationToken ct)
    {
        await healthChecks.AddAsync(check, ct);
        await healthChecks.SaveChangesAsync(ct);
        return check;
    }
}
