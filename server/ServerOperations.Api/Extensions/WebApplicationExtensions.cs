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
