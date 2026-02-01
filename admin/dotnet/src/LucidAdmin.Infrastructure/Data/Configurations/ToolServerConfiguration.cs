using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ToolServerConfiguration : IEntityTypeConfiguration<ToolServer>
{
    public void Configure(EntityTypeBuilder<ToolServer> builder)
    {
        builder.HasKey(ts => ts.Id);
        builder.HasIndex(ts => ts.Name).IsUnique();
        builder.HasIndex(ts => ts.Endpoint).IsUnique();

        builder.Property(ts => ts.Name).IsRequired().HasMaxLength(256);
        builder.Property(ts => ts.DisplayName).HasMaxLength(256);
        builder.Property(ts => ts.Endpoint).IsRequired().HasMaxLength(512);
        builder.Property(ts => ts.Domain).IsRequired().HasMaxLength(256);
        builder.Property(ts => ts.Description).HasMaxLength(1024);
        builder.Property(ts => ts.Version).HasMaxLength(64);
        builder.Property(ts => ts.ApiKeyHash).HasMaxLength(256);

        builder.HasMany(ts => ts.CapabilityMappings)
            .WithOne(cm => cm.ToolServer)
            .HasForeignKey(cm => cm.ToolServerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(ts => ts.AuditEvents)
            .WithOne(ae => ae.ToolServer)
            .HasForeignKey(ae => ae.ToolServerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
