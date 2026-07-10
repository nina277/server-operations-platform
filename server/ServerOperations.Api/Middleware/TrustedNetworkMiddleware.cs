using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Services.Interfaces;

namespace ServerOperations.Api.Middleware;

/// <summary>
/// 管理系エンドポイントへのアクセスを許可CIDRで制限するMiddleware。
/// CIDRが1件も登録されていない場合は制限しない(初期セットアップ用)。
/// UseAuthorizationの後に配置し、認可済みリクエストのみ検査する。
/// </summary>
public class TrustedNetworkMiddleware(RequestDelegate next)
{
    /// <summary>許可CIDR制限の対象となるパスプレフィックス。</summary>
    private static readonly PathString[] ProtectedPrefixes =
    [
        new("/api/v1/settings"),
    ];

    public async Task InvokeAsync(
        HttpContext context, INetworkCidrService cidrService, IAuditService audit)
    {
        var isProtected = ProtectedPrefixes.Any(p => context.Request.Path.StartsWithSegments(p));
        if (!isProtected)
        {
            await next(context);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress;
        if (await cidrService.IsAllowedAsync(remoteIp, context.RequestAborted))
        {
            await next(context);
            return;
        }

        await audit.RecordAsync(
            "network.access_denied", "Endpoint",
            $"{context.Request.Method} {context.Request.Path}", AuditResult.Denied,
            details: "remote IP outside trusted CIDR ranges");

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(
            "network_not_allowed", "この接続元からの管理操作は許可されていません。",
            ExceptionHandlingMiddleware.GetTraceId(context)));
    }
}
