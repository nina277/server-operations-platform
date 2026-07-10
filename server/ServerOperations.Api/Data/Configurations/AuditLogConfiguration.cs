using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Api.Models.Auth;

namespace ServerOperations.Api.Data.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.OccurredAt).HasColumnType("datetime(6)");
        builder.HasIndex(a => a.OccurredAt);

        builder.HasIndex(a => a.ActorUserId);

        builder.Property(a => a.ActorName).HasMaxLength(64);
        builder.Property(a => a.IpAddress).HasMaxLength(45).IsRequired();
        builder.Property(a => a.UserAgent).HasMaxLength(512).IsRequired();
        builder.Property(a => a.TargetType).HasMaxLength(64).IsRequired();
        builder.Property(a => a.TargetId).HasMaxLength(64);
        builder.Property(a => a.Action).HasMaxLength(64).IsRequired();
        builder.Property(a => a.Result).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(a => a.Details).HasMaxLength(2000);
        builder.Property(a => a.TraceId).HasMaxLength(64);
    }
}
