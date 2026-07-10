using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Settings;

[ApiController]
[Route("api/v1/settings/secrets")]
[Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
public class SecretsController(ISecretsService secretsService) : ControllerBase
{
    [HttpGet("{kind}/status")]
    public async Task<ActionResult<ApiResponse<SecretStatusDto>>> GetStatus(string kind, CancellationToken ct)
    {
        var result = await secretsService.GetStatusAsync(kind, ct);
        return Ok(ApiResponse<SecretStatusDto>.Ok(result, TraceId()));
    }

    [HttpPut("{kind}")]
    public async Task<ActionResult<ApiResponse<SecretStatusDto>>> Update(
        string kind, [FromBody] UpdateSecretRequest request, CancellationToken ct)
    {
        var result = await secretsService.UpdateAsync(kind, request.Value, ct);
        return Ok(ApiResponse<SecretStatusDto>.Ok(result, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
