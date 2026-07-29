using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/targets")]
[Authorize]
public class TargetsController(
    ITargetService targetService,
    ITelemetryService telemetryService) : ControllerBase
{
    [HttpGet("{id:long}/metrics")]
    public async Task<ActionResult<ApiResponse<List<MetricSnapshotDto>>>> GetMetrics(
        long id, [FromQuery] int limit = 100, CancellationToken ct = default)
    {
        var result = await telemetryService.GetMetricsAsync(id, limit, ct);
        return Ok(ApiResponse<List<MetricSnapshotDto>>.Ok(result, TraceId()));
    }

    [HttpGet("{id:long}/logs")]
    public async Task<ActionResult<ApiResponse<List<IncidentLogDto>>>> GetLogs(
        long id, [FromQuery] int limit = 50, CancellationToken ct = default)
    {
        var result = await telemetryService.GetLogsAsync(id, limit, ct);
        return Ok(ApiResponse<List<IncidentLogDto>>.Ok(result, TraceId()));
    }

    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<TargetDto>>>> GetAll(CancellationToken ct)
    {
        var result = await targetService.GetAllAsync(ct);
        return Ok(ApiResponse<List<TargetDto>>.Ok(result, TraceId()));
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiResponse<TargetDto>>> Get(long id, CancellationToken ct)
    {
        var result = await targetService.GetAsync(id, ct);
        return Ok(ApiResponse<TargetDto>.Ok(result, TraceId()));
    }

    [HttpGet("{id:long}/capabilities")]
    public async Task<ActionResult<ApiResponse<TargetCapabilitiesDto>>> GetCapabilities(
        long id, CancellationToken ct)
    {
        var result = await targetService.GetCapabilitiesAsync(id, ct);
        return Ok(ApiResponse<TargetCapabilitiesDto>.Ok(result, TraceId()));
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<TargetDto>>> Create(
        [FromBody] CreateTargetRequest request, CancellationToken ct)
    {
        var result = await targetService.CreateAsync(request, ct);
        return Ok(ApiResponse<TargetDto>.Ok(result, TraceId()));
    }

    [HttpPut("{id:long}")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<TargetDto>>> Update(
        long id, [FromBody] UpdateTargetRequest request, CancellationToken ct)
    {
        var result = await targetService.UpdateAsync(id, request, ct);
        return Ok(ApiResponse<TargetDto>.Ok(result, TraceId()));
    }

    [HttpPost("{id:long}/test-connection")]
    [Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
    public async Task<ActionResult<ApiResponse<ConnectionTestResultDto>>> TestConnection(
        long id, CancellationToken ct)
    {
        var result = await targetService.TestConnectionAsync(id, ct);
        return Ok(ApiResponse<ConnectionTestResultDto>.Ok(result, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
