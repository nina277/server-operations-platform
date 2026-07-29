using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Auth;

namespace ServerOperations.Core.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Username).HasMaxLength(64).IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.PasswordHash).HasMaxLength(100).IsRequired();

        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(32).IsRequired();

        builder.Property(u => u.CreatedAt).HasColumnType("datetime(6)");
        builder.Property(u => u.UpdatedAt).HasColumnType("datetime(6)");

        builder.HasOne(u => u.MfaCredential)
            .WithOne(m => m.User)
            .HasForeignKey<MfaCredential>(m => m.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(u => u.RefreshTokens)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
