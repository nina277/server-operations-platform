using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/diagnostic-rules")]
[Authorize]
public class DiagnosticRulesController(
    IDiagnosticRuleRepository rules,
    IRuleEngine ruleEngine) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<DiagnosticRuleDto>>>> GetAll(CancellationToken ct)
    {
        var all = await rules.GetAllAsync(ct);
        return Ok(ApiResponse<List<DiagnosticRuleDto>>.Ok(
            all.Select(DiagnosticRuleDto.From).ToList(), TraceId()));
    }

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

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
