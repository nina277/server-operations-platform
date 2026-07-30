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
        var me = await authService.GetMeAsync(CurrentUserId(), ct);
        return Ok(ApiResponse<MeResponse>.Ok(me, TraceId()));
    }

    /// <summary>
    /// 自分のパスワードを変更する。
    ///
    /// 役割を問わず自分のパスワードは変えられる必要があるため、MFA再認証は求めない。
    /// 代わりに現在のパスワードで本人確認し、変更後は他の端末のセッションを失効させる。
    /// </summary>
    [HttpPut("password")]
    [Authorize]
    public async Task<ActionResult<ApiResponse<ChangePasswordResponse>>> ChangePassword(
        [FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var result = await authService.ChangePasswordAsync(CurrentUserId(), request, ct);
        return Ok(ApiResponse<ChangePasswordResponse>.Ok(result, TraceId()));
    }

    private long CurrentUserId()
    {
        var value = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!long.TryParse(value, out var userId))
        {
            throw AppException.Unauthorized("invalid_token", "トークンが無効です。");
        }

        return userId;
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
