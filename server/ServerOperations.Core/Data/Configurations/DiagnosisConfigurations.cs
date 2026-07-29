using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Data.Configurations;

public class DiagnosticRuleConfiguration : IEntityTypeConfiguration<DiagnosticRule>
{
    public void Configure(EntityTypeBuilder<DiagnosticRule> builder)
    {
        builder.ToTable("diagnostic_rules");

        builder.HasKey(r => r.Id);
        builder.HasIndex(r => r.Classification);

        builder.Property(r => r.Name).HasMaxLength(100).IsRequired();
        builder.Property(r => r.Classification).HasMaxLength(64).IsRequired();
        builder.Property(r => r.RuleType).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.ConditionJson).HasMaxLength(2000).IsRequired();
        builder.Property(r => r.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(r => r.RecommendedActionId).HasMaxLength(64);
        builder.Property(r => r.RationaleTemplate).HasMaxLength(500).IsRequired();

        builder.Property(r => r.CreatedAt).HasColumnType("datetime(6)");
        builder.Property(r => r.UpdatedAt).HasColumnType("datetime(6)");
    }
}

public class DiagnosisConfiguration : IEntityTypeConfiguration<Diagnosis>
{
    public void Configure(EntityTypeBuilder<Diagnosis> builder)
    {
        builder.ToTable("diagnoses");

        builder.HasKey(d => d.Id);
        builder.HasIndex(d => d.IncidentId);
        builder.HasIndex(d => d.TargetId);

        builder.Property(d => d.Source).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.Classification).HasMaxLength(64).IsRequired();
        builder.Property(d => d.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.Rationale).HasMaxLength(2000).IsRequired();
        builder.Property(d => d.RecommendedActionId).HasMaxLength(64);

        builder.Property(d => d.CreatedAt).HasColumnType("datetime(6)");
    }
}
