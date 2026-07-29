using System.Security.Claims;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Middleware;

/// <summary>
/// 認可拒否(401/403)を自動的に監査ログへ記録するMiddleware。
/// 成功系の監査は各Serviceが業務文脈付きで記録する。
/// </summary>
public class AuditMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IAuditService audit)
    {
        await next(context);

        var status = context.Response.StatusCode;
        if (status is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden))
        {
            return;
        }

        // 認証APIの401はAuthService側で理由付きの監査を記録済みのため二重記録しない
        if (context.Request.Path.StartsWithSegments("/api/v1/auth"))
        {
            return;
        }

        var userIdValue = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        long? userId = long.TryParse(userIdValue, out var parsed) ? parsed : null;

        await audit.RecordAsync(
            action: "http.access_denied",
            targetType: "Endpoint",
            targetId: $"{context.Request.Method} {context.Request.Path}",
            result: AuditResult.Denied,
            actorUserId: userId,
            actorName: context.User.Identity?.Name,
            details: $"status={status}");
    }
}
