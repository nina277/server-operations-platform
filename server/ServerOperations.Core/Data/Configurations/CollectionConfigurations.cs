using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Data.Configurations;

public class MetricSnapshotConfiguration : IEntityTypeConfiguration<MetricSnapshot>
{
    public void Configure(EntityTypeBuilder<MetricSnapshot> builder)
    {
        builder.ToTable("metric_snapshots");

        builder.HasKey(m => m.Id);
        builder.HasIndex(m => new { m.TargetId, m.CollectedAt });

        builder.Property(m => m.CollectedAt).HasColumnType("datetime(6)");
        builder.Property(m => m.Kind).HasMaxLength(32).IsRequired();
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(m => m.PayloadJson).HasMaxLength(16000);
        builder.Property(m => m.ErrorMessage).HasMaxLength(1000);
    }
}

public class IncidentConfiguration : IEntityTypeConfiguration<Incident>
{
    public void Configure(EntityTypeBuilder<Incident> builder)
    {
        builder.ToTable("incidents");

        builder.HasKey(i => i.Id);
        builder.HasIndex(i => new { i.TargetId, i.SignatureSha256 });
        builder.HasIndex(i => i.Status);
        builder.HasIndex(i => i.LastOccurredAt);

        builder.Property(i => i.SignatureSha256).HasMaxLength(64).IsRequired();
        builder.Property(i => i.Title).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Classification).HasMaxLength(64).IsRequired();
        builder.Property(i => i.Service).HasMaxLength(200);
        builder.Property(i => i.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(i => i.FirstOccurredAt).HasColumnType("datetime(6)");
        builder.Property(i => i.LastOccurredAt).HasColumnType("datetime(6)");
        builder.Property(i => i.ResolvedAt).HasColumnType("datetime(6)");
        builder.Property(i => i.CreatedAt).HasColumnType("datetime(6)");
        builder.Property(i => i.UpdatedAt).HasColumnType("datetime(6)");
    }
}

public class IncidentLogConfiguration : IEntityTypeConfiguration<IncidentLog>
{
    public void Configure(EntityTypeBuilder<IncidentLog> builder)
    {
        builder.ToTable("incident_logs");

        builder.HasKey(l => l.Id);
        builder.HasIndex(l => new { l.TargetId, l.CollectedAt });
        builder.HasIndex(l => l.IncidentId);

        builder.Property(l => l.CollectedAt).HasColumnType("datetime(6)");
        builder.Property(l => l.Source).HasMaxLength(200).IsRequired();
        builder.Property(l => l.MaskedContent).HasMaxLength(16000).IsRequired();
    }
}
