using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Settings;

[ApiController]
[Route("api/v1/settings/network-cidrs")]
[Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
public class NetworkCidrsController(INetworkCidrService cidrService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<NetworkCidrDto>>>> GetAll(CancellationToken ct)
    {
        var result = await cidrService.GetAllAsync(ct);
        return Ok(ApiResponse<List<NetworkCidrDto>>.Ok(result, TraceId()));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<NetworkCidrDto>>> Add(
        [FromBody] CreateNetworkCidrRequest request, CancellationToken ct)
    {
        var result = await cidrService.AddAsync(request, ct);
        return Ok(ApiResponse<NetworkCidrDto>.Ok(result, TraceId()));
    }

    [HttpDelete("{id:long}")]
    public async Task<ActionResult<ApiResponse<object>>> Delete(long id, CancellationToken ct)
    {
        await cidrService.DeleteAsync(id, ct);
        return Ok(ApiResponse<object>.Ok(new { }, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
