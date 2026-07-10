using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Api.Models.Auth;

namespace ServerOperations.Api.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.HasIndex(t => t.FamilyId);

        builder.Property(t => t.CreatedByIp).HasMaxLength(45);
        builder.Property(t => t.ReplacedByTokenHash).HasMaxLength(64);

        builder.Property(t => t.ExpiresAt).HasColumnType("datetime(6)");
        builder.Property(t => t.CreatedAt).HasColumnType("datetime(6)");
        builder.Property(t => t.RevokedAt).HasColumnType("datetime(6)");
    }
}
