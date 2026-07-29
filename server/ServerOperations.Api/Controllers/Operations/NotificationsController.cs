using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.DTOs.Operations;
using ServerOperations.Api.Middleware;
using ServerOperations.Api.Services.Interfaces;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Repositories.Interfaces;

namespace ServerOperations.Api.Controllers.Operations;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public class NotificationsController(
    INotificationRepository notifications,
    IDeviceTokenRepository deviceTokens,
    IAuditService audit,
    ICurrentUserAccessor currentUser,
    TimeProvider timeProvider) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<PagedResult<NotificationDto>>>> Search(
        [FromQuery] bool? isRead, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var (items, total) = await notifications.SearchAsync(isRead, page, pageSize, ct);
        var result = new PagedResult<NotificationDto>(
            items.Select(NotificationDto.From).ToList(),
            Math.Max(page, 1), Math.Clamp(pageSize, 1, 100), total);
        return Ok(ApiResponse<PagedResult<NotificationDto>>.Ok(result, TraceId()));
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<ApiResponse<int>>> GetUnreadCount(CancellationToken ct)
    {
        var count = await notifications.CountUnreadAsync(ct);
        return Ok(ApiResponse<int>.Ok(count, TraceId()));
    }

    [HttpPatch("{id:long}/read")]
    public async Task<ActionResult<ApiResponse<NotificationDto>>> MarkAsRead(long id, CancellationToken ct)
    {
        var notification = await notifications.FindByIdAsync(id, ct)
            ?? throw AppException.NotFound("notification_not_found", "通知が見つかりません。");

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = timeProvider.GetUtcNow().UtcDateTime;
            await notifications.SaveChangesAsync(ct);
        }

        return Ok(ApiResponse<NotificationDto>.Ok(NotificationDto.From(notification), TraceId()));
    }

    /// <summary>Push通知の端末トークンを登録する。</summary>
    [HttpPost("device-tokens")]
    public async Task<ActionResult<ApiResponse<DeviceTokenDto>>> RegisterDeviceToken(
        [FromBody] RegisterDeviceTokenRequest request, CancellationToken ct)
    {
        var userId = CurrentUserId();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // 同じトークンが失効済みで再登録された場合は復活させる
        var existing = await deviceTokens.FindByTokenAsync(request.Token, ct);
        if (existing is not null)
        {
            existing.UserId = userId;
            existing.Label = request.Label;
            existing.IsActive = true;
            existing.RevokedAt = null;
            existing.ConsecutiveFailureCount = 0;
            await deviceTokens.SaveChangesAsync(ct);
            return Ok(ApiResponse<DeviceTokenDto>.Ok(DeviceTokenDto.From(existing), TraceId()));
        }

        var token = new DeviceToken
        {
            UserId = userId,
            Token = request.Token,
            Label = request.Label,
            IsActive = true,
            CreatedAt = now,
        };
        await deviceTokens.AddAsync(token, ct);
        await deviceTokens.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "notification.device_token.register", "DeviceToken", token.Id.ToString(), AuditResult.Success,
            actorUserId: userId, actorName: currentUser.Username, ct: ct);

        return Ok(ApiResponse<DeviceTokenDto>.Ok(DeviceTokenDto.From(token), TraceId()));
    }

    [HttpGet("device-tokens")]
    public async Task<ActionResult<ApiResponse<List<DeviceTokenDto>>>> GetDeviceTokens(CancellationToken ct)
    {
        var tokens = await deviceTokens.GetByUserAsync(CurrentUserId(), ct);
        return Ok(ApiResponse<List<DeviceTokenDto>>.Ok(
            tokens.Select(DeviceTokenDto.From).ToList(), TraceId()));
    }

    /// <summary>端末トークンを失効させる(自分の端末のみ)。</summary>
    [HttpDelete("device-tokens/{id:long}")]
    public async Task<ActionResult<ApiResponse<object>>> RevokeDeviceToken(long id, CancellationToken ct)
    {
        var userId = CurrentUserId();
        var token = await deviceTokens.FindByIdAsync(id, ct)
            ?? throw AppException.NotFound("device_token_not_found", "端末が見つかりません。");

        if (token.UserId != userId)
        {
            throw AppException.Forbidden("device_token_forbidden", "他の利用者の端末は操作できません。");
        }

        token.IsActive = false;
        token.RevokedAt = timeProvider.GetUtcNow().UtcDateTime;
        await deviceTokens.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "notification.device_token.revoke", "DeviceToken", token.Id.ToString(), AuditResult.Success,
            actorUserId: userId, actorName: currentUser.Username, ct: ct);

        return Ok(ApiResponse<object>.Ok(new { }, TraceId()));
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
