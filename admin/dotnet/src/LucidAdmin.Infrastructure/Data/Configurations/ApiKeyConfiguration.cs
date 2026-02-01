using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.HasKey(ak => ak.Id);

        // Unique constraint on KeyHash (for authentication lookups)
        builder.HasIndex(ak => ak.KeyHash).IsUnique();

        // Index on KeyPrefix (for display/logging lookups)
        builder.HasIndex(ak => ak.KeyPrefix);

        // Index on IsActive for filtering active keys
        builder.HasIndex(ak => ak.IsActive);

        // Index on AgentId for agent key lookups
        builder.HasIndex(ak => ak.AgentId);

        // Index on ToolServerId for tool server key lookups
        builder.HasIndex(ak => ak.ToolServerId);

        builder.Property(ak => ak.Name).IsRequired().HasMaxLength(100);
        builder.Property(ak => ak.Description).HasMaxLength(1024);
        builder.Property(ak => ak.KeyHash).IsRequired().HasMaxLength(64); // SHA256 = 64 hex chars
        builder.Property(ak => ak.KeyPrefix).IsRequired().HasMaxLength(20); // "lk_" + 8 chars + "..."
        builder.Property(ak => ak.Role).HasConversion<string>();
        builder.Property(ak => ak.CreatedAt).IsRequired();
        builder.Property(ak => ak.ExpiresAt);
        builder.Property(ak => ak.LastUsedAt);
        builder.Property(ak => ak.LastUsedFromIp).HasMaxLength(50);
        builder.Property(ak => ak.IsActive).IsRequired();
        builder.Property(ak => ak.RevokedAt);
        builder.Property(ak => ak.RevokedBy).HasMaxLength(256);
        builder.Property(ak => ak.RevocationReason).HasMaxLength(1024);
        builder.Property(ak => ak.CreatedBy).HasMaxLength(256);
        builder.Property(ak => ak.IpRestrictions).HasMaxLength(512);
        builder.Property(ak => ak.Metadata).HasColumnType("TEXT"); // JSON blob
        builder.Property(ak => ak.AllowedServiceAccountIds).HasColumnType("TEXT"); // JSON array

        // Relationships
        builder.HasOne(ak => ak.Agent)
            .WithMany()
            .HasForeignKey(ak => ak.AgentId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(ak => ak.ToolServer)
            .WithMany()
            .HasForeignKey(ak => ak.ToolServerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
