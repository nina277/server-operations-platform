using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Core.Data.Configurations;

public class MfaCredentialConfiguration : IEntityTypeConfiguration<MfaCredential>
{
    public void Configure(EntityTypeBuilder<MfaCredential> builder)
    {
        builder.ToTable("mfa_credentials");

        builder.HasKey(m => m.Id);

        builder.HasIndex(m => m.UserId).IsUnique();

        builder.Property(m => m.SecretProtected).HasMaxLength(512).IsRequired();

        builder.Property(m => m.CreatedAt).HasColumnType("datetime(6)");
        builder.Property(m => m.LastVerifiedAt).HasColumnType("datetime(6)");
    }
}
