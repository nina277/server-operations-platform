using Microsoft.EntityFrameworkCore;
using ServerOperations.Core.Models.Auth;
using ServerOperations.Core.Models.Operations;
using ServerOperations.Core.Models.Settings;

namespace ServerOperations.Core.Data;

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

    public DbSet<MetricSnapshot> MetricSnapshots => Set<MetricSnapshot>();

    public DbSet<Incident> Incidents => Set<Incident>();

    public DbSet<IncidentLog> IncidentLogs => Set<IncidentLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
