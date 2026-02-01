using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

/// <summary>
/// Entity Framework configuration for Ruleset entity.
/// </summary>
public class RulesetConfiguration : IEntityTypeConfiguration<Ruleset>
{
    public void Configure(EntityTypeBuilder<Ruleset> builder)
    {
        builder.HasKey(r => r.Id);

        // Indexes
        builder.HasIndex(r => r.Name).IsUnique();
        builder.HasIndex(r => r.Category);
        builder.HasIndex(r => r.IsActive);
        builder.HasIndex(r => r.IsBuiltIn);

        // Property configuration
        builder.Property(r => r.Name).IsRequired().HasMaxLength(256);
        builder.Property(r => r.DisplayName).HasMaxLength(256);
        builder.Property(r => r.Description).HasMaxLength(1024);
        builder.Property(r => r.Category).IsRequired().HasMaxLength(128);
        builder.Property(r => r.IsBuiltIn).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();

        // Rules relationship - cascade delete
        builder.HasMany(r => r.Rules)
            .WithOne(rule => rule.Ruleset)
            .HasForeignKey(rule => rule.RulesetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
