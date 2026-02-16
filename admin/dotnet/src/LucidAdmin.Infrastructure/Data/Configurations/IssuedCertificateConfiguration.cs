using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class IssuedCertificateConfiguration : IEntityTypeConfiguration<IssuedCertificate>
{
    public void Configure(EntityTypeBuilder<IssuedCertificate> builder)
    {
        builder.HasKey(c => c.Id);

        // Only one active cert per name (application-level + index for queries)
        builder.HasIndex(c => new { c.Name, c.IsActive });

        builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
        builder.Property(c => c.SubjectCN).IsRequired().HasMaxLength(500);
        builder.Property(c => c.SubjectAlternativeNames).HasMaxLength(2000);
        builder.Property(c => c.Thumbprint).IsRequired().HasMaxLength(128);
        builder.Property(c => c.SerialNumber).IsRequired().HasMaxLength(128);
        builder.Property(c => c.Usage).IsRequired().HasMaxLength(50);
        builder.Property(c => c.IssuedTo).HasMaxLength(200);
        builder.Property(c => c.CertPath).HasMaxLength(500);
        builder.Property(c => c.KeyPath).HasMaxLength(500);
        builder.Property(c => c.ReplacedByThumbprint).HasMaxLength(128);
    }
}
