using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Api.Models.Settings;

namespace ServerOperations.Api.Data.Configurations;

public class EncryptedSecretConfiguration : IEntityTypeConfiguration<EncryptedSecret>
{
    public void Configure(EntityTypeBuilder<EncryptedSecret> builder)
    {
        builder.ToTable("encrypted_secrets");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Kind).HasMaxLength(64).IsRequired();
        builder.HasIndex(s => s.Kind).IsUnique();

        builder.Property(s => s.ValueProtected).HasMaxLength(8000).IsRequired();

        builder.Property(s => s.UpdatedAt).HasColumnType("datetime(6)");
    }
}
