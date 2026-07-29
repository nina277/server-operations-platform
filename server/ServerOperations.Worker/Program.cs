using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Adapters.Implementations;
using ServerOperations.Core.Adapters.Interfaces;
using ServerOperations.Core.Data;
using ServerOperations.Core.Repositories.Implementations;
using ServerOperations.Core.Repositories.Interfaces;
using ServerOperations.Core.Services;
using ServerOperations.Worker;
using ServerOperations.Worker.Jobs;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);

// APIと同じ鍵リングを共有し、APIが暗号化した対象資格情報を復号できるようにする
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("server-operations-platform");
var keysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(keysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

var connectionString = builder.Configuration.GetConnectionString("Default");
var hangfireEnabled = !string.IsNullOrWhiteSpace(connectionString)
    && builder.Configuration.GetValue("Hangfire:Enabled", defaultValue: true);

if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 4, 0))));

    builder.Services.AddScoped<IMonitoringTargetRepository, MonitoringTargetRepository>();
    builder.Services.AddScoped<IMetricSnapshotRepository, MetricSnapshotRepository>();
    builder.Services.AddScoped<IIncidentRepository, IncidentRepository>();
    builder.Services.AddScoped<IIncidentLogRepository, IncidentLogRepository>();
    builder.Services.AddScoped<ITargetCollectionService, TargetCollectionService>();

    builder.Services.AddScoped<IDiagnosticRuleRepository, DiagnosticRuleRepository>();
    builder.Services.AddScoped<IDiagnosisRepository, DiagnosisRepository>();
    builder.Services.AddSingleton<IAdapterTemplateCatalog, AdapterTemplateCatalog>();
    builder.Services.AddSingleton<IRuleEngine, RuleEngine>();
    builder.Services.AddScoped<IDiagnosisService, DiagnosisService>();

    // 復旧の実行はWorkerだけが行う
    builder.Services.AddSingleton(
        builder.Configuration.GetSection(RecoveryLimits.SectionName).Get<RecoveryLimits>() ?? new RecoveryLimits());
    builder.Services.AddScoped<IRecoveryActionRepository, RecoveryActionRepository>();
    builder.Services.AddScoped<IRecoveryApprovalRepository, RecoveryApprovalRepository>();
    builder.Services.AddScoped<IHealthCheckRepository, HealthCheckRepository>();
    builder.Services.AddSingleton<IRecoveryActionCatalog, RecoveryActionCatalog>();
    builder.Services.AddScoped<IRecoveryRateLimiter, RecoveryRateLimiter>();
    builder.Services.AddScoped<IHealthCheckService, HealthCheckService>();
    builder.Services.AddScoped<IRecoveryExecutionService, RecoveryExecutionService>();

    // アダプター用HTTPクライアント(接続時にも遮断対象IPを検査する)
    builder.Services.AddHttpClient(DockerAdapter.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(15))
        .ConfigurePrimaryHttpMessageHandler(AdapterHttpHandlerFactory.CreateGuardedHandler);
    builder.Services.AddHttpClient(HttpAdapter.HttpClientName, client =>
            client.Timeout = TimeSpan.FromSeconds(65))
        .ConfigurePrimaryHttpMessageHandler(AdapterHttpHandlerFactory.CreateGuardedHandler);
    builder.Services.AddScoped<IDockerAdapter, DockerAdapter>();
    builder.Services.AddScoped<IHttpAdapter, HttpAdapter>();
}

if (hangfireEnabled)
{
    builder.Services.AddHangfire(config => config
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseStorage(new MySqlStorage(
            connectionString + "Allow User Variables=true;",
            new MySqlStorageOptions
            {
                TablesPrefix = "hangfire_",
            })));
    builder.Services.AddHangfireServer(options =>
    {
        options.WorkerCount = 4;
        // recoveryを先頭に置き、復旧を収集より優先して処理する
        options.Queues = ["recovery", "collection", "default"];
    });

    // 対象別の定期収集ジョブを同期させるスケジューラー
    builder.Services.AddHostedService<CollectionJobScheduler>();
}
else
{
    builder.Services.AddHostedService<Worker>();
}

var host = builder.Build();
host.Run();
