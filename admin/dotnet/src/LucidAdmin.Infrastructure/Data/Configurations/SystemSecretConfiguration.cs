using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class SystemSecretConfiguration : IEntityTypeConfiguration<SystemSecret>
{
    public void Configure(EntityTypeBuilder<SystemSecret> builder)
    {
        builder.HasKey(s => s.Id);

        builder.HasIndex(s => s.Name).IsUnique();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(100);
        builder.Property(s => s.EncryptedValue).IsRequired();
        builder.Property(s => s.Nonce).IsRequired();
        builder.Property(s => s.Purpose).HasMaxLength(500);
        builder.Property(s => s.Metadata).HasMaxLength(2000);
    }
}
