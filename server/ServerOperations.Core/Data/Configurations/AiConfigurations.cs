using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Data.Configurations;

public class AiUsageRecordConfiguration : IEntityTypeConfiguration<AiUsageRecord>
{
    public void Configure(EntityTypeBuilder<AiUsageRecord> builder)
    {
        builder.ToTable("ai_usage_records");

        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.CalledAt);
        builder.HasIndex(r => r.IncidentId);

        builder.Property(r => r.Provider).HasMaxLength(32).IsRequired();
        builder.Property(r => r.Model).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Result).HasConversion<string>().HasMaxLength(24).IsRequired();
        builder.Property(r => r.ErrorSummary).HasMaxLength(500);

        builder.Property(r => r.CalledAt).HasColumnType("datetime(6)");
    }
}

public class AiUsageLimitConfiguration : IEntityTypeConfiguration<AiUsageLimit>
{
    public void Configure(EntityTypeBuilder<AiUsageLimit> builder)
    {
        builder.ToTable("ai_usage_limits");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Provider).HasMaxLength(32).IsRequired();
        builder.Property(l => l.Model).HasMaxLength(64).IsRequired();

        builder.Property(l => l.UpdatedAt).HasColumnType("datetime(6)");
    }
}
