using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/incidents")]
[Authorize]
public class IncidentsController(
    IIncidentService incidentService,
    IDiagnosisRepository diagnoses) : ControllerBase
{
    [HttpGet("{id:long}/diagnoses")]
    public async Task<ActionResult<ApiResponse<List<DiagnosisDto>>>> GetDiagnoses(
        long id, CancellationToken ct)
    {
        // インシデントの存在確認を兼ねる(見つからなければ404)
        await incidentService.GetAsync(id, ct);
        var items = await diagnoses.GetByIncidentAsync(id, ct);
        return Ok(ApiResponse<List<DiagnosisDto>>.Ok(
            items.Select(DiagnosisDto.From).ToList(), TraceId()));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<IncidentDto>>>> Search(
        [FromQuery] IncidentListQuery query, CancellationToken ct)
    {
        var result = await incidentService.SearchAsync(query, ct);
        return Ok(ApiResponse<PagedResult<IncidentDto>>.Ok(result, TraceId()));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<IncidentDto>>> Get(long id, CancellationToken ct)
    {
        var result = await incidentService.GetAsync(id, ct);
        return Ok(ApiResponse<IncidentDto>.Ok(result, TraceId()));
    }

    [HttpPatch("{id:long}/status")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<IncidentDto>>> UpdateStatus(
        long id, [FromBody] UpdateIncidentStatusRequest request, CancellationToken ct)
    {
        var result = await incidentService.UpdateStatusAsync(id, request.Status, ct);
        return Ok(ApiResponse<IncidentDto>.Ok(result, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
