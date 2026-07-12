using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize]
public class DashboardController(IDashboardService dashboardService) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<ApiResponse<DashboardSummaryDto>>> GetSummary(CancellationToken ct)
    {
        var result = await dashboardService.GetSummaryAsync(ct);
        return Ok(ApiResponse<DashboardSummaryDto>.Ok(
            result, ExceptionHandlingMiddleware.GetTraceId(HttpContext)));
    }
}
