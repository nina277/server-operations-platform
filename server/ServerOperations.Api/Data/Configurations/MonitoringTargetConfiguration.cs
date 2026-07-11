using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Api.Models.Operations;

namespace ServerOperations.Api.Data.Configurations;

public class MonitoringTargetConfiguration : IEntityTypeConfiguration<MonitoringTarget>
{
    public void Configure(EntityTypeBuilder<MonitoringTarget> builder)
    {
        builder.ToTable("monitoring_targets");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(t => t.Name).IsUnique();

        builder.Property(t => t.TemplateId).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);

        builder.Property(t => t.CreatedAt).HasColumnType("datetime(6)");
        builder.Property(t => t.UpdatedAt).HasColumnType("datetime(6)");

        builder.HasOne(t => t.Profile)
            .WithOne(p => p.Target)
            .HasForeignKey<TargetProfile>(p => p.TargetId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(t => t.Credentials)
            .WithOne(c => c.Target)
            .HasForeignKey(c => c.TargetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TargetProfileConfiguration : IEntityTypeConfiguration<TargetProfile>
{
    public void Configure(EntityTypeBuilder<TargetProfile> builder)
    {
        builder.ToTable("target_profiles");

        builder.HasKey(p => p.Id);
        builder.HasIndex(p => p.TargetId).IsUnique();

        builder.Property(p => p.SettingsJson).HasMaxLength(8000).IsRequired();
        builder.Property(p => p.UpdatedAt).HasColumnType("datetime(6)");
    }
}

public class TargetCredentialConfiguration : IEntityTypeConfiguration<TargetCredential>
{
    public void Configure(EntityTypeBuilder<TargetCredential> builder)
    {
        builder.ToTable("target_credentials");

        builder.HasKey(c => c.Id);
        builder.HasIndex(c => new { c.TargetId, c.Kind }).IsUnique();

        builder.Property(c => c.Kind).HasMaxLength(64).IsRequired();
        builder.Property(c => c.ValueProtected).HasMaxLength(4000).IsRequired();
        builder.Property(c => c.UpdatedAt).HasColumnType("datetime(6)");
    }
}
