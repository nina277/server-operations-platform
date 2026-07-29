using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Settings;

namespace ServerOperations.Core.Data.Configurations;

public class BackupRunConfiguration : IEntityTypeConfiguration<BackupRun>
{
    public void Configure(EntityTypeBuilder<BackupRun> builder)
    {
        builder.ToTable("backup_runs");

        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.StartedAt);

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.ObjectKey).HasMaxLength(500);
        builder.Property(r => r.Message).HasMaxLength(1000);

        builder.Property(r => r.StartedAt).HasColumnType("datetime(6)");
        builder.Property(r => r.CompletedAt).HasColumnType("datetime(6)");
    }
}
