using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ServiceAccountConfiguration : IEntityTypeConfiguration<ServiceAccount>
{
    public void Configure(EntityTypeBuilder<ServiceAccount> builder)
    {
        builder.HasKey(sa => sa.Id);

        // Unique constraint on Name only (provider info is in Configuration now)
        builder.HasIndex(sa => sa.Name).IsUnique();

        // Index on Provider for filtering by provider type
        builder.HasIndex(sa => sa.Provider);

        builder.Property(sa => sa.Name).IsRequired().HasMaxLength(100);
        builder.Property(sa => sa.DisplayName).HasMaxLength(256);
        builder.Property(sa => sa.Description).HasMaxLength(1024);

        builder.Property(sa => sa.Provider).IsRequired().HasMaxLength(50);
        builder.Property(sa => sa.AccountType).IsRequired().HasMaxLength(50);
        builder.Property(sa => sa.Configuration).HasColumnType("TEXT");  // JSON blob

        builder.Property(sa => sa.CredentialStorage).HasConversion<string>();
        builder.Property(sa => sa.CredentialReference).HasMaxLength(512);

        // Database credential storage (encrypted)
        builder.Property(sa => sa.EncryptedCredentials).HasColumnType("BLOB");
        builder.Property(sa => sa.CredentialNonce).HasColumnType("BLOB");
        builder.Property(sa => sa.CredentialsUpdatedAt);

        builder.Property(sa => sa.HealthStatus).HasConversion<string>();
        builder.Property(sa => sa.LastHealthMessage).HasMaxLength(1024);

        builder.HasMany(sa => sa.CapabilityMappings)
            .WithOne(cm => cm.ServiceAccount)
            .HasForeignKey(cm => cm.ServiceAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
