using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ServerOperations.Api.DTOs.Common;
using ServerOperations.Api.Extensions;
using ServerOperations.Api.Middleware;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

// モデル検証エラーもApiResponse形式で返す
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var message = context.ModelState
            .SelectMany(kv => kv.Value?.Errors.Select(e => e.ErrorMessage) ?? [])
            .FirstOrDefault(m => !string.IsNullOrEmpty(m) && !m.Contains("deserialization"))
            ?? "リクエストの形式が正しくありません。";
        return new BadRequestObjectResult(
            ApiResponse<object>.Fail("validation_error", message, context.HttpContext.TraceIdentifier));
    };
});

builder.Services.AddAppServices(builder.Configuration);
builder.Services.AddAppAuthentication(builder.Configuration);

// nginx(リバースプロキシ)経由の実クライアントIPをX-Forwarded-Forから取得する。
// APIは外部ポートを公開せずnginxからのみ到達できるため、直近プロキシを信頼する。
// ForwardLimit=1により、クライアントが偽装したXFFではなくnginxが付与した末尾の値を採用する。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"]);

var app = builder.Build();

app.UseForwardedHeaders();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// liveness: プロセスが応答できるか。readiness: 依存関係を含め受付可能か。
app.MapHealthChecks("/api/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
});
app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<AuditMiddleware>();
app.UseMiddleware<TrustedNetworkMiddleware>();

app.MapControllers();

await app.InitializeDatabaseAsync();

app.Run();

// WebApplicationFactoryによる統合テストからエントリポイントを参照可能にする
public partial class Program;
