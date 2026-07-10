using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddHealthChecks()
    .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live", "ready"]);

var app = builder.Build();

// liveness: プロセスが応答できるか。readiness: 依存関係を含め受付可能か。
// T-00時点では依存チェックはなく、DB等への依存が入るチケットで"ready"タグのチェックを追加する。
app.MapHealthChecks("/api/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live"),
});
app.MapHealthChecks("/api/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

app.MapControllers();

app.Run();
