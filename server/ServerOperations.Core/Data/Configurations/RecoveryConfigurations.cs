using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Data.Configurations;

public class RecoveryActionConfiguration : IEntityTypeConfiguration<RecoveryAction>
{
    public void Configure(EntityTypeBuilder<RecoveryAction> builder)
    {
        builder.ToTable("recovery_actions");

        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.IncidentId);
        builder.HasIndex(a => new { a.TargetId, a.RequestedAt });

        // 同一Idempotency-Keyの重複実行を防ぐ(DB制約でも保証する)
        builder.HasIndex(a => a.IdempotencyKey).IsUnique();

        builder.Property(a => a.ActionId).HasMaxLength(64).IsRequired();
        builder.Property(a => a.TargetResource).HasMaxLength(200);
        builder.Property(a => a.RiskLevel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(a => a.IdempotencyKey).HasMaxLength(128);
        builder.Property(a => a.ResultMessage).HasMaxLength(1000);
        builder.Property(a => a.BlockedReason).HasMaxLength(200);

        builder.Property(a => a.RequestedAt).HasColumnType("datetime(6)");
        builder.Property(a => a.StartedAt).HasColumnType("datetime(6)");
        builder.Property(a => a.CompletedAt).HasColumnType("datetime(6)");
    }
}

public class RecoveryApprovalConfiguration : IEntityTypeConfiguration<RecoveryApproval>
{
    public void Configure(EntityTypeBuilder<RecoveryApproval> builder)
    {
        builder.ToTable("recovery_approvals");

        builder.HasKey(a => a.Id);
        builder.HasIndex(a => a.IncidentId);

        builder.Property(a => a.ActionId).HasMaxLength(64).IsRequired();
        builder.Property(a => a.TargetResource).HasMaxLength(200);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.DecidedByUsername).HasMaxLength(64);
        builder.Property(a => a.Comment).HasMaxLength(500);

        builder.Property(a => a.DecidedAt).HasColumnType("datetime(6)");
        builder.Property(a => a.ExpiresAt).HasColumnType("datetime(6)");
        builder.Property(a => a.CreatedAt).HasColumnType("datetime(6)");
    }
}

public class HealthCheckConfiguration : IEntityTypeConfiguration<HealthCheck>
{
    public void Configure(EntityTypeBuilder<HealthCheck> builder)
    {
        builder.ToTable("health_checks");

        builder.HasKey(h => h.Id);
        builder.HasIndex(h => new { h.TargetId, h.CheckedAt });
        builder.HasIndex(h => h.RecoveryActionId);

        builder.Property(h => h.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(h => h.Message).HasMaxLength(1000).IsRequired();

        builder.Property(h => h.CheckedAt).HasColumnType("datetime(6)");
    }
}
