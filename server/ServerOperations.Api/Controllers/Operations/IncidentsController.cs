using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/incidents")]
[Authorize]
public class IncidentsController(IIncidentService incidentService) : ControllerBase
{
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
