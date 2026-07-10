using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Api.Models.Settings;

namespace ServerOperations.Api.Data.Configurations;

public class TrustedNetworkCidrConfiguration : IEntityTypeConfiguration<TrustedNetworkCidr>
{
    public void Configure(EntityTypeBuilder<TrustedNetworkCidr> builder)
    {
        builder.ToTable("trusted_network_cidrs");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Cidr).HasMaxLength(64).IsRequired();
        builder.HasIndex(c => c.Cidr).IsUnique();

        builder.Property(c => c.Description).HasMaxLength(200);

        builder.Property(c => c.CreatedAt).HasColumnType("datetime(6)");
    }
}
