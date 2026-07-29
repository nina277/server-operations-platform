using System.Diagnostics;
using ServerOperations.Api.DTOs.Common;

namespace ServerOperations.Api.Middleware;

/// <summary>
/// 未処理例外をApiResponse形式へ変換する。内部情報(スタックトレース、例外メッセージ)は応答へ出さない。
/// </summary>
public class ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (AppException ex)
        {
            var traceId = GetTraceId(context);
            logger.LogWarning(ex, "Handled application error. traceId={TraceId} code={Code}", traceId, ex.Code);
            await WriteErrorAsync(context, ex.StatusCode, ex.Code, ex.Message, traceId);
        }
        catch (Exception ex)
        {
            var traceId = GetTraceId(context);
            logger.LogError(ex, "Unhandled exception. traceId={TraceId}", traceId);
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                "サーバー内部でエラーが発生しました。",
                traceId);
        }
    }

    internal static string GetTraceId(HttpContext context) =>
        Activity.Current?.Id ?? context.TraceIdentifier;

    private static async Task WriteErrorAsync(
        HttpContext context, int statusCode, string code, string message, string traceId)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(ApiResponse<object>.Fail(code, message, traceId));
    }
}
