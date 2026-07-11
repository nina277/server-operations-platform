using Microsoft.EntityFrameworkCore;
using ServerOperations.Api.Models.Auth;
using ServerOperations.Api.Models.Operations;
using ServerOperations.Api.Models.Settings;

namespace ServerOperations.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<MfaCredential> MfaCredentials => Set<MfaCredential>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();

    public DbSet<EncryptedSecret> EncryptedSecrets => Set<EncryptedSecret>();

    public DbSet<TrustedNetworkCidr> TrustedNetworkCidrs => Set<TrustedNetworkCidr>();

    public DbSet<MonitoringTarget> MonitoringTargets => Set<MonitoringTarget>();

    public DbSet<TargetProfile> TargetProfiles => Set<TargetProfile>();

    public DbSet<TargetCredential> TargetCredentials => Set<TargetCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
