using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Settings;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Settings;

[ApiController]
[Route("api/v1/settings")]
[Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
public class SettingsController(ISettingsService settingsService) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<ApiResponse<ProfileSettingsDto>>> GetProfile(CancellationToken ct)
    {
        var result = await settingsService.GetProfileAsync(ct);
        return Ok(ApiResponse<ProfileSettingsDto>.Ok(result, TraceId()));
    }

    [HttpPut("profile")]
    public async Task<ActionResult<ApiResponse<ProfileSettingsDto>>> UpdateProfile(
        [FromBody] ProfileSettingsDto request, CancellationToken ct)
    {
        var result = await settingsService.UpdateProfileAsync(request, ct);
        return Ok(ApiResponse<ProfileSettingsDto>.Ok(result, TraceId()));
    }

    [HttpGet("retention")]
    public async Task<ActionResult<ApiResponse<RetentionSettingsDto>>> GetRetention(CancellationToken ct)
    {
        var result = await settingsService.GetRetentionAsync(ct);
        return Ok(ApiResponse<RetentionSettingsDto>.Ok(result, TraceId()));
    }

    [HttpPut("retention")]
    public async Task<ActionResult<ApiResponse<RetentionSettingsDto>>> UpdateRetention(
        [FromBody] RetentionSettingsDto request, CancellationToken ct)
    {
        var result = await settingsService.UpdateRetentionAsync(request, ct);
        return Ok(ApiResponse<RetentionSettingsDto>.Ok(result, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
