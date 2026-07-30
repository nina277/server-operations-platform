using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Services.Implementations;

public class DiagnosticRuleService(
    IDiagnosticRuleRepository rules,
    IRecoveryActionCatalog actionCatalog,
    IRuleEngine ruleEngine,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : IDiagnosticRuleService
{
    public async Task<List<DiagnosticRuleDto>> GetAllAsync(CancellationToken ct = default)
    {
        var all = await rules.GetAllAsync(ct);
        return all.Select(DiagnosticRuleDto.From).ToList();
    }

    public async Task<DiagnosticRuleDto> GetAsync(long id, CancellationToken ct = default) =>
        DiagnosticRuleDto.From(await FindOrThrowAsync(id, ct));

    public async Task<DiagnosticRuleDto> CreateAsync(
        SaveDiagnosticRuleRequest request, CancellationToken ct = default)
    {
        var parsed = await ValidateAsync(request, excludeId: null, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;

        var rule = new DiagnosticRule
        {
            Name = request.Name.Trim(),
            Classification = request.Classification.Trim(),
            RuleType = parsed.RuleType,
            ConditionJson = request.ConditionJson,
            Severity = parsed.Severity,
            RecommendedActionId = parsed.RecommendedActionId,
            Priority = request.Priority,
            RationaleTemplate = request.RationaleTemplate,
            IsEnabled = request.IsEnabled,
            CreatedAt = now,
            UpdatedAt = now,
        };

        await rules.AddAsync(rule, ct);
        await rules.SaveChangesAsync(ct);

        await RecordAsync("diagnostic_rule.create", rule.Id, AuditResult.Success,
            Describe(rule), ct);

        return DiagnosticRuleDto.From(rule);
    }

    public async Task<DiagnosticRuleDto> UpdateAsync(
        long id, SaveDiagnosticRuleRequest request, CancellationToken ct = default)
    {
        var rule = await FindOrThrowAsync(id, ct);
        var parsed = await ValidateAsync(request, excludeId: id, ct);

        // 変更前後を監査に残す(何がどう変わったかを後から追えるようにする)
        var before = Describe(rule);

        rule.Name = request.Name.Trim();
        rule.Classification = request.Classification.Trim();
        rule.RuleType = parsed.RuleType;
        rule.ConditionJson = request.ConditionJson;
        rule.Severity = parsed.Severity;
        rule.RecommendedActionId = parsed.RecommendedActionId;
        rule.Priority = request.Priority;
        rule.RationaleTemplate = request.RationaleTemplate;
        rule.IsEnabled = request.IsEnabled;
        rule.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        await rules.SaveChangesAsync(ct);

        await RecordAsync("diagnostic_rule.update", rule.Id, AuditResult.Success,
            $"変更前: {before} / 変更後: {Describe(rule)}", ct);

        return DiagnosticRuleDto.From(rule);
    }

    public async Task<DiagnosticRuleDto> SetEnabledAsync(
        long id, bool isEnabled, CancellationToken ct = default)
    {
        var rule = await FindOrThrowAsync(id, ct);

        if (rule.IsEnabled != isEnabled)
        {
            rule.IsEnabled = isEnabled;
            rule.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;
            await rules.SaveChangesAsync(ct);
        }

        await RecordAsync("diagnostic_rule.set_enabled", rule.Id, AuditResult.Success,
            $"{rule.Name}: {(isEnabled ? "有効" : "無効")}", ct);

        return DiagnosticRuleDto.From(rule);
    }

    public async Task<RuleTestResponse> TestAsync(
        RuleTestRequest request, CancellationToken ct = default)
    {
        var enabled = await rules.GetEnabledAsync(ct);

        // 編集中のルールが渡されたら、それも評価に含める。
        // 保存済みに同じIdがあれば置き換える(編集後の状態で確かめるため)。
        long? candidateId = null;
        if (request.CandidateRule is { } candidate)
        {
            var rule = BuildCandidate(candidate);
            candidateId = rule.Id;

            enabled = enabled.Where(r => r.Id != rule.Id).ToList();
            enabled.Add(rule);
        }

        var matches = ruleEngine.Evaluate(enabled, request.ToContext());

        return new RuleTestResponse
        {
            Matches = matches.Select(m => new RuleTestMatchDto
            {
                RuleId = m.Rule.Id,
                RuleName = m.Rule.Name,
                Classification = m.Rule.Classification,
                Severity = m.Rule.Severity.ToString(),
                RecommendedActionId = m.Rule.RecommendedActionId,
                Rationale = m.Rationale,
                IsCandidate = candidateId is { } id && m.Rule.Id == id,
            }).ToList(),
        };
    }

    /// <summary>
    /// 編集中のルールを評価用に組み立てる。保存はしない。
    /// 検証は保存時と同じものを通すため、保存できない条件では試験もできない。
    /// </summary>
    private DiagnosticRule BuildCandidate(CandidateRuleDto candidate)
    {
        var parsed = ValidateContent(
            candidate.RuleType, candidate.Severity, candidate.ConditionJson,
            candidate.RecommendedActionId);

        return new DiagnosticRule
        {
            // 新規(Id=0)のときは既存ルールと衝突しない負のIdを使い、結果で見分けられるようにする
            Id = candidate.Id > 0 ? candidate.Id : -1,
            Name = candidate.Name,
            Classification = candidate.Classification,
            RuleType = parsed.RuleType,
            ConditionJson = candidate.ConditionJson,
            Severity = parsed.Severity,
            RecommendedActionId = parsed.RecommendedActionId,
            Priority = candidate.Priority,
            RationaleTemplate = candidate.RationaleTemplate,
            // 無効なルールを編集中でも判定を確かめられるようにする
            IsEnabled = true,
        };
    }

    public RuleEditorOptionsDto GetEditorOptions() => new()
    {
        Fields = RuleConditionValidator.AllowedFields,
        Operators = RuleConditionValidator.AllowedOperators,
        RuleTypes = Enum.GetNames<DiagnosticRuleType>(),
        Severities = Enum.GetNames<IncidentSeverity>(),
        // 推奨アクションに指定できるのは復旧の許可リストにあるIDだけ
        RecommendedActionIds = actionCatalog.GetAll().Select(d => d.ActionId).ToList(),
    };

    private async Task<DiagnosticRule> FindOrThrowAsync(long id, CancellationToken ct) =>
        await rules.FindByIdAsync(id, ct)
        ?? throw AppException.NotFound("diagnostic_rule_not_found", "診断ルールが見つかりません。");

    private sealed record ParsedRule(
        DiagnosticRuleType RuleType, IncidentSeverity Severity, string? RecommendedActionId);

    /// <summary>
    /// 保存前の検証。
    /// 条件は種別ごとの構造で確かめ、推奨アクションは復旧の許可リストにあるIDだけを受け付ける。
    /// </summary>
    private async Task<ParsedRule> ValidateAsync(
        SaveDiagnosticRuleRequest request, long? excludeId, CancellationToken ct)
    {
        var parsed = ValidateContent(
            request.RuleType, request.Severity, request.ConditionJson, request.RecommendedActionId);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw AppException.BadRequest("invalid_name", "名前を入力してください。");
        }

        if (await rules.ExistsByNameAsync(request.Name.Trim(), excludeId, ct))
        {
            throw AppException.Conflict("duplicate_rule_name", "同じ名前のルールが既にあります。");
        }

        return parsed;
    }

    /// <summary>
    /// ルールの中身の検証。保存と試験の両方で同じ判定を通す。
    /// 試験だけ緩くすると「試験は通るが保存できない」条件が生まれ、確認の意味がなくなる。
    /// </summary>
    private ParsedRule ValidateContent(
        string ruleTypeText, string severityText, string conditionJson, string? recommendedActionIdText)
    {
        if (!Enum.TryParse<DiagnosticRuleType>(ruleTypeText, ignoreCase: true, out var ruleType))
        {
            throw AppException.BadRequest("invalid_rule_type",
                $"ルール種別は次のいずれかを指定してください: {string.Join(" / ", Enum.GetNames<DiagnosticRuleType>())}");
        }

        if (!Enum.TryParse<IncidentSeverity>(severityText, ignoreCase: true, out var severity))
        {
            throw AppException.BadRequest("invalid_severity",
                $"深刻度は次のいずれかを指定してください: {string.Join(" / ", Enum.GetNames<IncidentSeverity>())}");
        }

        var condition = RuleConditionValidator.Validate(ruleType, conditionJson);
        if (!condition.IsValid)
        {
            throw AppException.BadRequest("invalid_condition", condition.Error!);
        }

        // 推奨アクションは自由記述を受け付けない。許可リストに無いIDは拒否する。
        string? recommendedActionId = null;
        if (!string.IsNullOrWhiteSpace(recommendedActionIdText))
        {
            var candidate = recommendedActionIdText.Trim();
            if (actionCatalog.Find(candidate) is null)
            {
                throw AppException.BadRequest("invalid_recommended_action",
                    "推奨アクションは復旧の許可リストにあるIDのみ指定できます。");
            }

            recommendedActionId = candidate;
        }

        return new ParsedRule(ruleType, severity, recommendedActionId);
    }

    /// <summary>監査に残す要約。条件は収集値の判定式のみで、秘密情報は含まない。</summary>
    private static string Describe(DiagnosticRule rule) =>
        $"{rule.Name} / {rule.Classification} / {rule.RuleType} / {rule.Severity} / "
        + $"優先度{rule.Priority} / 推奨={rule.RecommendedActionId ?? "なし"} / "
        + $"{(rule.IsEnabled ? "有効" : "無効")} / 条件={rule.ConditionJson}";

    private Task RecordAsync(
        string action, long ruleId, AuditResult result, string details, CancellationToken ct) =>
        audit.RecordAsync(
            action, "DiagnosticRule", ruleId.ToString(), result,
            actorUserId: currentUser.UserId, actorName: currentUser.Username,
            details: details, ct: ct);
}
