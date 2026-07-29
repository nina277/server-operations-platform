using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Data.Configurations;

public class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");

        builder.HasKey(n => n.Id);
        builder.HasIndex(n => n.LastNotifiedAt);
        builder.HasIndex(n => new { n.AggregationKey, n.LastNotifiedAt });
        builder.HasIndex(n => n.IsRead);

        builder.Property(n => n.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(n => n.Title).HasMaxLength(200).IsRequired();
        builder.Property(n => n.Body).HasMaxLength(2000).IsRequired();
        builder.Property(n => n.AggregationKey).HasMaxLength(128);

        builder.Property(n => n.FirstNotifiedAt).HasColumnType("datetime(6)");
        builder.Property(n => n.LastNotifiedAt).HasColumnType("datetime(6)");
        builder.Property(n => n.ReadAt).HasColumnType("datetime(6)");

        builder.HasMany(n => n.Deliveries)
            .WithOne(d => d.Notification)
            .HasForeignKey(d => d.NotificationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries");

        builder.HasKey(d => d.Id);
        builder.HasIndex(d => d.NotificationId);

        builder.Property(d => d.Channel).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(d => d.ErrorMessage).HasMaxLength(500);

        builder.Property(d => d.AttemptedAt).HasColumnType("datetime(6)");
    }
}

public class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("device_tokens");

        builder.HasKey(t => t.Id);
        builder.HasIndex(t => t.Token).IsUnique();
        builder.HasIndex(t => new { t.UserId, t.IsActive });

        builder.Property(t => t.Token).HasMaxLength(512).IsRequired();
        builder.Property(t => t.Label).HasMaxLength(100);

        builder.Property(t => t.CreatedAt).HasColumnType("datetime(6)");
        builder.Property(t => t.LastUsedAt).HasColumnType("datetime(6)");
        builder.Property(t => t.RevokedAt).HasColumnType("datetime(6)");
    }
}
