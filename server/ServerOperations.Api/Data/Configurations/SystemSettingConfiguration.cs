using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Api.Models.Settings;

namespace ServerOperations.Api.Data.Configurations;

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> builder)
    {
        builder.ToTable("system_settings");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.HasIndex(s => s.Category).IsUnique();

        builder.Property(s => s.Value).HasMaxLength(8000).IsRequired();

        builder.Property(s => s.UpdatedAt).HasColumnType("datetime(6)");
    }
}
