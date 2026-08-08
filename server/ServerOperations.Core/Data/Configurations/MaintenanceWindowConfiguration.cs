using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Data.Configurations;

public class MaintenanceWindowConfiguration : IEntityTypeConfiguration<MaintenanceWindow>
{
    public void Configure(EntityTypeBuilder<MaintenanceWindow> builder)
    {
        builder.ToTable("maintenance_windows");

        builder.HasKey(w => w.Id);

        builder.Property(w => w.Reason).HasMaxLength(200).IsRequired();

        builder.Property(w => w.StartsAt).HasColumnType("datetime(6)");
        builder.Property(w => w.EndsAt).HasColumnType("datetime(6)");
        builder.Property(w => w.CancelledAt).HasColumnType("datetime(6)");
        builder.Property(w => w.CreatedAt).HasColumnType("datetime(6)");

        // 抑止の判定は「いま効いている期間があるか」を毎回引くため、
        // 終了時刻から絞れるようにする。
        builder.HasIndex(w => w.EndsAt);
        builder.HasIndex(w => new { w.TargetId, w.EndsAt });

        builder.HasOne(w => w.Target)
            .WithMany()
            .HasForeignKey(w => w.TargetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class IncidentNoteConfiguration : IEntityTypeConfiguration<IncidentNote>
{
    public void Configure(EntityTypeBuilder<IncidentNote> builder)
    {
        builder.ToTable("incident_notes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.AuthorName).HasMaxLength(64).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(4000).IsRequired();
        builder.Property(n => n.CreatedAt).HasColumnType("datetime(6)");

        builder.HasIndex(n => new { n.IncidentId, n.CreatedAt });

        builder.HasOne(n => n.Incident)
            .WithMany()
            .HasForeignKey(n => n.IncidentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
