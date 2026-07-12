using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Auth;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Auth;

[ApiController]
[Route("api/v1/me")]
public class MeController(IAuthService authService) : ControllerBase
{
    [HttpGet]
    [Authorize]
    public async Task<ActionResult<ApiResponse<MeResponse>>> Get(CancellationToken ct)
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(value, out var userId))
        {
            throw AppException.Unauthorized("invalid_token", "トークンが無効です。");
        }

        var me = await authService.GetMeAsync(userId, ct);
        return Ok(ApiResponse<MeResponse>.Ok(me, ExceptionHandlingMiddleware.GetTraceId(HttpContext)));
    }
}
