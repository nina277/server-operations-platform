using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ServerOperations.Core.Models.Operations;

namespace ServerOperations.Core.Data.Configurations;

public class ServiceTemplateConfiguration : IEntityTypeConfiguration<ServiceTemplate>
{
    public void Configure(EntityTypeBuilder<ServiceTemplate> builder)
    {
        builder.ToTable("service_templates");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Key).HasMaxLength(64).IsRequired();
        builder.Property(t => t.Name).HasMaxLength(100).IsRequired();
        builder.Property(t => t.Description).HasMaxLength(500);
        builder.Property(t => t.Image).HasMaxLength(300).IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnType("datetime(6)");
        builder.Property(t => t.UpdatedAt).HasColumnType("datetime(6)");

        // 識別子で引くため一意にする
        builder.HasIndex(t => t.Key).IsUnique();

        builder.HasMany(t => t.Inputs)
            .WithOne(i => i.Template!)
            .HasForeignKey(i => i.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ServiceTemplateInputConfiguration : IEntityTypeConfiguration<ServiceTemplateInput>
{
    public void Configure(EntityTypeBuilder<ServiceTemplateInput> builder)
    {
        builder.ToTable("service_template_inputs");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Key).HasMaxLength(64).IsRequired();
        builder.Property(i => i.Label).HasMaxLength(100).IsRequired();
        builder.Property(i => i.DefaultValue).HasMaxLength(300);
        builder.Property(i => i.Description).HasMaxLength(300);
        builder.Property(i => i.ContainerPath).HasMaxLength(300);
        builder.Property(i => i.Type).HasConversion<string>().HasMaxLength(16);

        builder.HasIndex(i => new { i.TemplateId, i.Key }).IsUnique();
    }
}

public class DeployedServiceConfiguration : IEntityTypeConfiguration<DeployedService>
{
    public void Configure(EntityTypeBuilder<DeployedService> builder)
    {
        builder.ToTable("deployed_services");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.ContainerName).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Image).HasMaxLength(300).IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(s => s.Message).HasMaxLength(500);
        // 入力値は件数が読めないため longtext(行サイズ上限に効かせない)
        builder.Property(s => s.InputsJson).HasColumnType("longtext");
        builder.Property(s => s.DeployedAt).HasColumnType("datetime(6)");
        builder.Property(s => s.UpdatedAt).HasColumnType("datetime(6)");

        builder.HasIndex(s => s.TargetId);
        builder.HasIndex(s => s.ContainerName);

        builder.HasOne(s => s.Target).WithMany()
            .HasForeignKey(s => s.TargetId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(s => s.Template).WithMany()
            .HasForeignKey(s => s.TemplateId).OnDelete(DeleteBehavior.SetNull);
    }
}
