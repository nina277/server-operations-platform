using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Services.Implementations;

public class TargetService(
    IMonitoringTargetRepository targets,
    IAdapterTemplateCatalog catalog,
    IDockerAdapter dockerAdapter,
    IHttpAdapter httpAdapter,
    IDataProtectionProvider dataProtectionProvider,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ITargetService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector("TargetCredential");

    public async Task<List<TargetDto>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await targets.GetAllAsync(ct);
        return all.Select(ToDto).ToList();
    }

    public async Task<TargetDto> GetAsync(long id, CancellationToken ct = default) =>
        ToDto(await FindOrThrowAsync(id, ct));

    public async Task<TargetDto> CreateAsync(CreateTargetRequest request, CancellationToken ct = default)
    {
        var template = catalog.Find(request.TemplateId)
            ?? throw AppException.BadRequest("unknown_template", "不明なテンプレートIDです。");

        if (await targets.FindByNameAsync(request.Name, ct) is not null)
        {
            throw AppException.Conflict("duplicate_target_name", "同じ名前の監視対象が既に存在します。");
        }

        var settings = await ValidateInputsAsync(template, request.Settings, request.Credentials, ct);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var target = new MonitoringTarget
        {
            Name = request.Name,
            TemplateId = template.Id,
            Description = request.Description,
            IsEnabled = true,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedByUserId = currentUser.UserId,
            Profile = new TargetProfile
            {
                SettingsJson = JsonSerializer.Serialize(settings, JsonOptions),
                UpdatedAt = now,
            },
        };

        foreach (var (kind, value) in request.Credentials.Where(c => !string.IsNullOrEmpty(c.Value)))
        {
            target.Credentials.Add(new TargetCredential
            {
                Kind = kind,
                ValueProtected = _protector.Protect(value),
                UpdatedAt = now,
            });
        }

        await targets.AddAsync(target, ct);
        await targets.SaveChangesAsync(ct);

        // 監査に設定値そのものは残さない(URLに認証情報が含まれる可能性を考慮し、キー名のみ)
        await audit.RecordAsync(
            "target.create", "MonitoringTarget", target.Id.ToString(), AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"name={target.Name} template={template.Id} settingsKeys=[{string.Join(',', settings.Keys)}]",
            ct: ct);

        return ToDto(target);
    }

    public async Task<TargetDto> UpdateAsync(long id, UpdateTargetRequest request, CancellationToken ct = default)
    {
        var target = await FindOrThrowAsync(id, ct);
        var template = catalog.Find(target.TemplateId)
            ?? throw AppException.BadRequest("unknown_template", "不明なテンプレートIDです。");

        var duplicate = await targets.FindByNameAsync(request.Name, ct);
        if (duplicate is not null && duplicate.Id != id)
        {
            throw AppException.Conflict("duplicate_target_name", "同じ名前の監視対象が既に存在します。");
        }

        var settings = await ValidateInputsAsync(template, request.Settings, request.Credentials, ct,
            existingCredentialKinds: target.Credentials.Select(c => c.Kind).ToHashSet());

        var now = timeProvider.GetUtcNow().UtcDateTime;
        target.Name = request.Name;
        target.Description = request.Description;
        target.IsEnabled = request.IsEnabled;
        target.UpdatedAt = now;

        if (target.Profile is null)
        {
            target.Profile = new TargetProfile
            {
                TargetId = target.Id,
                SettingsJson = JsonSerializer.Serialize(settings, JsonOptions),
                UpdatedAt = now,
            };
        }
        else
        {
            target.Profile.SettingsJson = JsonSerializer.Serialize(settings, JsonOptions);
            target.Profile.UpdatedAt = now;
        }

        foreach (var (kind, value) in request.Credentials.Where(c => !string.IsNullOrEmpty(c.Value)))
        {
            var existing = target.Credentials.FirstOrDefault(c => c.Kind == kind);
            if (existing is null)
            {
                target.Credentials.Add(new TargetCredential
                {
                    TargetId = target.Id,
                    Kind = kind,
                    ValueProtected = _protector.Protect(value),
                    UpdatedAt = now,
                });
            }
            else
            {
                existing.ValueProtected = _protector.Protect(value);
                existing.UpdatedAt = now;
            }
        }

        await targets.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "target.update", "MonitoringTarget", target.Id.ToString(), AuditResult.Success,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: $"name={target.Name} enabled={target.IsEnabled} settingsKeys=[{string.Join(',', settings.Keys)}]",
            ct: ct);

        return ToDto(target);
    }

    public async Task<TargetCapabilitiesDto> GetCapabilitiesAsync(long id, CancellationToken ct = default)
    {
        var target = await FindOrThrowAsync(id, ct);
        var template = catalog.Find(target.TemplateId)
            ?? throw AppException.BadRequest("unknown_template", "不明なテンプレートIDです。");

        return new TargetCapabilitiesDto
        {
            TargetId = target.Id,
            TemplateId = template.Id,
            Capabilities = template.Capabilities,
            AllowedOperations = template.AllowedOperations,
            RecommendedMonitors = template.RecommendedMonitors,
            InitialRules = template.InitialRules,
        };
    }

    public async Task<ConnectionTestResultDto> TestConnectionAsync(long id, CancellationToken ct = default)
    {
        var target = await FindOrThrowAsync(id, ct);
        var settings = ReadSettings(target);

        AdapterConnectionResult result;
        switch (target.TemplateId)
        {
            case AdapterTemplateCatalog.DockerHost:
            case AdapterTemplateCatalog.DockerComposeApp:
            {
                var endpoint = GetRequiredSetting(settings, "endpoint");
                await EndpointValidator.ValidateDockerEndpointAsync(endpoint, ct);
                result = await dockerAdapter.TestConnectionAsync(endpoint, ct);
                break;
            }

            case AdapterTemplateCatalog.WebSite:
            {
                var url = GetRequiredSetting(settings, "url");
                await EndpointValidator.ValidateHttpUrlAsync(url, ct);
                result = await httpAdapter.TestConnectionAsync(new HttpCheckOptions
                {
                    Url = url,
                    ExpectedStatus = ParseIntSetting(settings, "expectedStatus", 200),
                    TimeoutSeconds = ParseIntSetting(settings, "timeoutSeconds", 10),
                    BasicAuthUser = settings.GetValueOrDefault("basicAuthUser"),
                    BasicAuthPassword = UnprotectCredential(target, "basicAuthPassword"),
                }, ct);
                break;
            }

            default:
                throw AppException.BadRequest("unknown_template", "このテンプレートの接続試験は未対応です。");
        }

        await audit.RecordAsync(
            "target.test_connection", "MonitoringTarget", target.Id.ToString(),
            result.Success ? AuditResult.Success : AuditResult.Failure,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: result.Message, ct: ct);

        return new ConnectionTestResultDto
        {
            Success = result.Success,
            Message = result.Message,
            LatencyMs = result.LatencyMs,
            Detail = result.Detail,
        };
    }

    private async Task<Dictionary<string, string>> ValidateInputsAsync(
        AdapterTemplate template,
        Dictionary<string, string> settings,
        Dictionary<string, string> credentials,
        CancellationToken ct,
        HashSet<string>? existingCredentialKinds = null)
    {
        var knownKeys = template.Inputs.Select(i => i.Key).ToHashSet();

        var unknown = settings.Keys.Concat(credentials.Keys).FirstOrDefault(k => !knownKeys.Contains(k));
        if (unknown is not null)
        {
            throw AppException.BadRequest("unknown_input", $"テンプレートに存在しない入力です: {unknown}");
        }

        var normalized = new Dictionary<string, string>();

        foreach (var input in template.Inputs)
        {
            if (input.Secret)
            {
                if (settings.ContainsKey(input.Key))
                {
                    throw AppException.BadRequest(
                        "secret_in_settings", $"秘密値の入力はcredentialsで指定してください: {input.Key}");
                }

                var hasValue = credentials.TryGetValue(input.Key, out var secret) && !string.IsNullOrEmpty(secret)
                    || (existingCredentialKinds?.Contains(input.Key) ?? false);
                if (input.Required && !hasValue)
                {
                    throw AppException.BadRequest("missing_required_input", $"必須入力が不足しています: {input.Key}");
                }

                continue;
            }

            var value = settings.GetValueOrDefault(input.Key);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (input.Required)
                {
                    throw AppException.BadRequest("missing_required_input", $"必須入力が不足しています: {input.Key}");
                }

                if (input.DefaultValue is not null)
                {
                    normalized[input.Key] = input.DefaultValue;
                }

                continue;
            }

            value = value.Trim();

            switch (input.Type)
            {
                case TemplateInputType.Int when !int.TryParse(value, out _):
                    throw AppException.BadRequest("invalid_input_type", $"{input.Key} は整数で指定してください。");
                case TemplateInputType.Bool when !bool.TryParse(value, out _):
                    throw AppException.BadRequest("invalid_input_type", $"{input.Key} は true/false で指定してください。");
                case TemplateInputType.Url:
                    if (input.Key == "endpoint")
                    {
                        await EndpointValidator.ValidateDockerEndpointAsync(value, ct);
                    }
                    else
                    {
                        await EndpointValidator.ValidateHttpUrlAsync(value, ct);
                    }

                    break;
            }

            normalized[input.Key] = value;
        }

        return normalized;
    }

    private async Task<MonitoringTarget> FindOrThrowAsync(long id, CancellationToken ct) =>
        await targets.FindByIdAsync(id, ct)
            ?? throw AppException.NotFound("target_not_found", "監視対象が見つかりません。");

    private static Dictionary<string, string> ReadSettings(MonitoringTarget target) =>
        target.Profile is null
            ? []
            : JsonSerializer.Deserialize<Dictionary<string, string>>(target.Profile.SettingsJson, JsonOptions) ?? [];

    private static string GetRequiredSetting(Dictionary<string, string> settings, string key) =>
        settings.GetValueOrDefault(key)
            ?? throw AppException.BadRequest("missing_required_input", $"設定が不足しています: {key}");

    private static int ParseIntSetting(Dictionary<string, string> settings, string key, int fallback) =>
        int.TryParse(settings.GetValueOrDefault(key), out var parsed) ? parsed : fallback;

    private string? UnprotectCredential(MonitoringTarget target, string kind)
    {
        var credential = target.Credentials.FirstOrDefault(c => c.Kind == kind);
        return credential is null ? null : _protector.Unprotect(credential.ValueProtected);
    }

    private static TargetDto ToDto(MonitoringTarget target) => new()
    {
        Id = target.Id,
        Name = target.Name,
        TemplateId = target.TemplateId,
        Description = target.Description,
        IsEnabled = target.IsEnabled,
        Settings = ReadSettings(target),
        ConfiguredCredentials = target.Credentials.Select(c => c.Kind).OrderBy(k => k).ToList(),
        CreatedAt = target.CreatedAt,
        UpdatedAt = target.UpdatedAt,
    };
}
