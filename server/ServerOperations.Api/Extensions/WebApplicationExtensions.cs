using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Data;
using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Api.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// DB初期化。Database:AutoMigrate=true でマイグレーションを適用し、
    /// ユーザーが1人もいない場合のみ環境変数から初期管理者を作成する。
    /// </summary>
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        if (!app.Configuration.GetValue<bool>("Database:AutoMigrate"))
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>()
            .CreateLogger("DatabaseInitializer");

        await db.Database.MigrateAsync();
        logger.LogInformation("Database migration applied.");

        // 基本診断ルール(ContainerStopped / HttpUnavailable / MemoryPressure / DiskPressure)。
        // 初回だけでなく、版を上げて既定ルールが増えたときも足りない分を投入する。
        // 有効・無効に関わらず全件の名前で照合するため、無効にしたルールは復活しない。
        var existingRuleNames = await db.DiagnosticRules.Select(rule => rule.Name).ToListAsync();
        var missingRules = ServerOperations.Core.Services.DefaultDiagnosticRules.Missing(
            existingRuleNames, DateTime.UtcNow);
        if (missingRules.Count > 0)
        {
            db.DiagnosticRules.AddRange(missingRules);
            await db.SaveChangesAsync();
            logger.LogInformation(
                "Default diagnostic rules added: {Names}",
                string.Join(", ", missingRules.Select(rule => rule.Name)));
        }

        // サービステンプレートも、既定ルールと同じ考え方で足りない分だけ投入する。
        // 利用者が消したものは復活させない
        var existingTemplateKeys = await db.ServiceTemplates.Select(t => t.Key).ToListAsync();
        var missingTemplates = ServerOperations.Core.Services.Deployment.DefaultServiceTemplates
            .Missing(existingTemplateKeys, DateTime.UtcNow);
        if (missingTemplates.Count > 0)
        {
            db.ServiceTemplates.AddRange(missingTemplates);
            await db.SaveChangesAsync();
            logger.LogInformation(
                "Default service templates added: {Keys}",
                string.Join(", ", missingTemplates.Select(t => t.Key)));
        }

        if (await db.Users.AnyAsync())
        {
            return;
        }

        var username = app.Configuration["InitialAdmin:Username"];
        var password = app.Configuration["InitialAdmin:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "No users exist and InitialAdmin:Username / InitialAdmin:Password are not set. " +
                "Set them (e.g. env InitialAdmin__Username) to create the first administrator.");
            return;
        }

        if (password.Length < 12)
        {
            logger.LogWarning("InitialAdmin:Password must be at least 12 characters. Admin user was NOT created.");
            return;
        }

        var now = DateTime.UtcNow;
        db.Users.Add(new User
        {
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRole.OperatorAdmin,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Initial administrator '{Username}' created.", username);
    }
}
