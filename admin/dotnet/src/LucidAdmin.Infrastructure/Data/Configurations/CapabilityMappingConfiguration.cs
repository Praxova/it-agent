using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class CapabilityMappingConfiguration : IEntityTypeConfiguration<CapabilityMapping>
{
    public void Configure(EntityTypeBuilder<CapabilityMapping> builder)
    {
        builder.HasKey(cm => cm.Id);

        // Unique constraint: one service account can have one mapping per capability per tool server
        builder.Property(cm => cm.CapabilityId).IsRequired().HasMaxLength(100);
        builder.HasIndex(cm => new { cm.ServiceAccountId, cm.ToolServerId, cm.CapabilityId }).IsUnique();

        builder.Property(cm => cm.CapabilityVersion).HasMaxLength(20);
        builder.Property(cm => cm.Configuration).HasColumnType("TEXT");
        builder.Property(cm => cm.AllowedScopesJson).HasColumnType("TEXT");
        builder.Property(cm => cm.DeniedScopesJson).HasColumnType("TEXT");

        builder.Property(cm => cm.HealthStatus).HasConversion<string>();
        builder.Property(cm => cm.LastHealthMessage).HasMaxLength(1024);

        builder.HasOne(cm => cm.ServiceAccount)
            .WithMany(sa => sa.CapabilityMappings)
            .HasForeignKey(cm => cm.ServiceAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cm => cm.ToolServer)
            .WithMany(ts => ts.CapabilityMappings)
            .HasForeignKey(cm => cm.ToolServerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(cm => cm.Capability)
            .WithMany(c => c.Mappings)
            .HasForeignKey(cm => cm.CapabilityId)
            .HasPrincipalKey(c => c.CapabilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
