using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Auth;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Auth;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(IAuthService authService, IMfaService mfaService) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenPairResponse>>> Login(
        [FromBody] LoginRequest request, CancellationToken ct)
    {
        var pair = await authService.LoginAsync(request, ct);
        return Ok(ApiResponse<TokenPairResponse>.Ok(pair, TraceId()));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<TokenPairResponse>>> Refresh(
        [FromBody] RefreshRequest request, CancellationToken ct)
    {
        var pair = await authService.RefreshAsync(request.RefreshToken, ct);
        return Ok(ApiResponse<TokenPairResponse>.Ok(pair, TraceId()));
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<ActionResult<ApiResponse<object>>> Logout(
        [FromBody] LogoutRequest request, CancellationToken ct)
    {
        await authService.LogoutAsync(request.RefreshToken, ct);
        return Ok(ApiResponse<object>.Ok(new { }, TraceId()));
    }

    [HttpPost("mfa/setup")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<MfaSetupResponse>>> MfaSetup(CancellationToken ct)
    {
        var result = await mfaService.SetupAsync(CurrentUserId(), ct);
        return Ok(ApiResponse<MfaSetupResponse>.Ok(result, TraceId()));
    }

    [HttpPost("mfa/verify")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<MfaVerifyResponse>>> MfaVerify(
        [FromBody] MfaVerifyRequest request, CancellationToken ct)
    {
        var result = await mfaService.VerifyAsync(CurrentUserId(), request.TotpCode, ct);
        return Ok(ApiResponse<MfaVerifyResponse>.Ok(result, TraceId()));
    }

    private long CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return long.TryParse(value, out var id)
            ? id
            : throw AppException.Unauthorized("invalid_token", "トークンが無効です。");
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
