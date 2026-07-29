using Microsoft.Extensions.Logging;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Core.Services;

public interface IDiagnosisService
{
    /// <summary>
    /// インシデントを診断する。1) ルール評価 → 2) 障害署名が一致する過去診断の再利用。
    /// どちらも失敗した場合はnull(T-08でAI診断へ縮退する)。
    /// </summary>
    Task<Diagnosis?> DiagnoseAsync(Incident incident, DiagnosticContext context, CancellationToken ct = default);
}

public class DiagnosisService(
    IDiagnosticRuleRepository rules,
    IDiagnosisRepository diagnoses,
    IMonitoringTargetRepository targets,
    IAdapterTemplateCatalog catalog,
    IRuleEngine ruleEngine,
    TimeProvider timeProvider,
    ILogger<DiagnosisService> logger) : IDiagnosisService
{
    public async Task<Diagnosis?> DiagnoseAsync(
        Incident incident, DiagnosticContext context, CancellationToken ct = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var allowedOperations = await GetAllowedOperationsAsync(incident.TargetId, ct);

        // 1) ルール評価。最優先の一致を採用する
        var enabledRules = await rules.GetEnabledAsync(ct);
        var matches = ruleEngine.Evaluate(enabledRules, context);
        if (matches.Count > 0)
        {
            var top = matches[0];
            var diagnosis = new Diagnosis
            {
                IncidentId = incident.Id,
                TargetId = incident.TargetId,
                Source = DiagnosisSource.Rule,
                RuleId = top.Rule.Id,
                Classification = top.Rule.Classification,
                Severity = top.Rule.Severity,
                Rationale = top.Rationale,
                RecommendedActionId = top.Rule.RecommendedActionId,
                RecommendedActionAllowed = IsActionAllowed(top.Rule.RecommendedActionId, allowedOperations),
                CreatedAt = now,
            };
            await diagnoses.AddAsync(diagnosis, ct);
            await diagnoses.SaveChangesAsync(ct);
            return diagnosis;
        }

        // 2) 履歴再利用。署名が一致する過去診断を候補として提示する。
        //    過去に許可されていた操作でも、現在の対象能力・許可操作・危険度で必ず再検証する。
        var past = await diagnoses.FindLatestBySignatureAsync(incident.SignatureSha256, incident.Id, ct);
        if (past is not null)
        {
            var diagnosis = new Diagnosis
            {
                IncidentId = incident.Id,
                TargetId = incident.TargetId,
                Source = DiagnosisSource.History,
                ReusedDiagnosisId = past.Id,
                Classification = past.Classification,
                Severity = past.Severity,
                Rationale = $"同一の障害署名を持つ過去の診断(#{past.Id})を再利用: {past.Rationale}",
                RecommendedActionId = past.RecommendedActionId,
                RecommendedActionAllowed = IsActionAllowed(past.RecommendedActionId, allowedOperations),
                CreatedAt = now,
            };
            await diagnoses.AddAsync(diagnosis, ct);
            await diagnoses.SaveChangesAsync(ct);
            return diagnosis;
        }

        logger.LogInformation(
            "No rule matched and no reusable history for incident {IncidentId} (signature {Signature})",
            incident.Id, incident.SignatureSha256);
        return null;
    }

    /// <summary>現在の対象テンプレートが許可する操作一覧(テンプレート定義に厳密に従う)。</summary>
    private async Task<HashSet<string>> GetAllowedOperationsAsync(long targetId, CancellationToken ct)
    {
        var target = await targets.FindByIdAsync(targetId, ct);
        var template = target is null ? null : catalog.Find(target.TemplateId);
        return new HashSet<string>(template?.AllowedOperations ?? []);
    }

    private static bool IsActionAllowed(string? actionId, HashSet<string> allowedOperations) =>
        actionId is not null && allowedOperations.Contains(actionId);
}
