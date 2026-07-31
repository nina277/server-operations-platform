using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Auth;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Controllers.Auth;

/// <summary>
/// 利用者の管理。役割の割り当ては権限そのものを動かす操作のため、
/// 参照も含めて運用管理者 + MFA再認証を要求する。
///
/// 削除の口は用意しない。監査ログは操作者IDを持つため、
/// 利用者を消すと過去の記録から誰の操作か辿れなくなる。
/// </summary>
[ApiController]
[Route("api/v1/users")]
[Authorize(Policy = AuthorizationPolicies.AdminWithRecentMfa)]
public class UsersController(IUserManagementService usersService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<List<ManagedUserDto>>>> GetAll(CancellationToken ct)
    {
        var result = await usersService.GetAllAsync(ct);
        return Ok(ApiResponse<List<ManagedUserDto>>.Ok(result, TraceId()));
    }

    [HttpPost]
    public async Task<ActionResult<ApiResponse<ManagedUserDto>>> Create(
        [FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var result = await usersService.CreateAsync(request, ct);
        return Ok(ApiResponse<ManagedUserDto>.Ok(result, TraceId()));
    }

    [HttpPatch("{id:long}/role")]
    public async Task<ActionResult<ApiResponse<ManagedUserDto>>> UpdateRole(
        long id, [FromBody] UpdateUserRoleRequest request, CancellationToken ct)
    {
        var result = await usersService.UpdateRoleAsync(id, request, ct);
        return Ok(ApiResponse<ManagedUserDto>.Ok(result, TraceId()));
    }

    [HttpPatch("{id:long}/active")]
    public async Task<ActionResult<ApiResponse<ManagedUserDto>>> UpdateActive(
        long id, [FromBody] UpdateUserActiveRequest request, CancellationToken ct)
    {
        var result = await usersService.UpdateActiveAsync(id, request, ct);
        return Ok(ApiResponse<ManagedUserDto>.Ok(result, TraceId()));
    }

    /// <summary>
    /// 他人のMFAを解除する。端末を失ったときの回復手段。
    /// 対象の全セッションを失効させたうえで監査に残す。
    /// </summary>
    [HttpPost("{id:long}/mfa/reset")]
    public async Task<ActionResult<ApiResponse<ManagedUserDto>>> ResetMfa(
        long id, CancellationToken ct)
    {
        var result = await usersService.ResetMfaAsync(id, ct);
        return Ok(ApiResponse<ManagedUserDto>.Ok(result, TraceId()));
    }

    private string TraceId() => ExceptionHandlingMiddleware.GetTraceId(HttpContext);
}
