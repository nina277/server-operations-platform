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

    public DbSet<IncidentNote> IncidentNotes => Set<IncidentNote>();

    public DbSet<MaintenanceWindow> MaintenanceWindows => Set<MaintenanceWindow>();

    public DbSet<DiagnosticRule> DiagnosticRules => Set<DiagnosticRule>();

    public DbSet<Diagnosis> Diagnoses => Set<Diagnosis>();

    public DbSet<RecoveryAction> RecoveryActions => Set<RecoveryAction>();

    public DbSet<RecoveryApproval> RecoveryApprovals => Set<RecoveryApproval>();

    public DbSet<HealthCheck> HealthChecks => Set<HealthCheck>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();

    public DbSet<DeviceToken> DeviceTokens => Set<DeviceToken>();

    public DbSet<BackupRun> BackupRuns => Set<BackupRun>();

    public DbSet<AiUsageRecord> AiUsageRecords => Set<AiUsageRecord>();

    public DbSet<AiUsageLimit> AiUsageLimits => Set<AiUsageLimit>();

    public DbSet<ServiceTemplate> ServiceTemplates => Set<ServiceTemplate>();

    public DbSet<ServiceTemplateInput> ServiceTemplateInputs => Set<ServiceTemplateInput>();

    public DbSet<DeployedService> DeployedServices => Set<DeployedService>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
