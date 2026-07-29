using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/diagnostic-rules")]
[Authorize]
public class DiagnosticRulesController(
    IDiagnosticRuleRepository rules,
    IDiagnosticRuleService ruleService,
    IRuleEngine ruleEngine) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<DiagnosticRuleDto>>>> GetAll(CancellationToken ct)
    {
        var all = await ruleService.GetAllAsync(ct);
        return Ok(ApiResponse<List<DiagnosticRuleDto>>.Ok(all, TraceId()));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<DiagnosticRuleDto>>> Get(long id, CancellationToken ct)
    {
        var rule = await ruleService.GetAsync(id, ct);
        return Ok(ApiResponse<DiagnosticRuleDto>.Ok(rule, TraceId()));
    }

    /// <summary>ルールを書くときに選べる値。画面の入力欄を組み立てるために返す。</summary>
    [HttpGet("editor-options")]
    public ActionResult<ApiResponse<RuleEditorOptionsDto>> GetEditorOptions() =>
        Ok(ApiResponse<RuleEditorOptionsDto>.Ok(ruleService.GetEditorOptions(), TraceId()));

    /// <summary>ルールのテスト入力。判定結果と根拠を返す(保存しない)。</summary>
    [HttpPost("test")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<RuleTestResponse>>> Test(
        [FromBody] RuleTestRequest request, CancellationToken ct)
    {
        var enabled = await rules.GetEnabledAsync(ct);
        var matches = ruleEngine.Evaluate(enabled, request.ToContext());

        var response = new RuleTestResponse
        {
            Matches = matches.Select(m => new RuleTestMatchDto
            {
                RuleId = m.Rule.Id,
                RuleName = m.Rule.Name,
                Classification = m.Rule.Classification,
                Severity = m.Rule.Severity.ToString(),
                RecommendedActionId = m.Rule.RecommendedActionId,
                Rationale = m.Rationale,
            }).ToList(),
        };
        return Ok(ApiResponse<RuleTestResponse>.Ok(response, TraceId()));
    }

    /// <summary>
    /// ルールを作る。ルールは自動復旧の入口のため、管理者 + MFA再認証を必要とする。
    /// 条件は保存前に種別ごとに検証し、推奨アクションは復旧の許可リストにあるIDのみ受け付ける。
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<DiagnosticRuleDto>>> Create(
        [FromBody] SaveDiagnosticRuleRequest request, CancellationToken ct)
    {
        var rule = await ruleService.CreateAsync(request, ct);
        return Ok(ApiResponse<DiagnosticRuleDto>.Ok(rule, TraceId()));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<DiagnosticRuleDto>>> Update(
        long id, [FromBody] SaveDiagnosticRuleRequest request, CancellationToken ct)
    {
        var rule = await ruleService.UpdateAsync(id, request, ct);
        return Ok(ApiResponse<DiagnosticRuleDto>.Ok(rule, TraceId()));
    }

    /// <summary>有効・無効の切り替え。ルールを消さずに止められるようにする。</summary>
    [HttpPatch("{id:long}/enabled")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<DiagnosticRuleDto>>> SetEnabled(
        long id, [FromBody] UpdateRuleEnabledRequest request, CancellationToken ct)
    {
        var rule = await ruleService.SetEnabledAsync(id, request.IsEnabled, ct);
        return Ok(ApiResponse<DiagnosticRuleDto>.Ok(rule, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
