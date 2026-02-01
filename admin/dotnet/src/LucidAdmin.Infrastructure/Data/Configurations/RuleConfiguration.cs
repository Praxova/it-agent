using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

/// <summary>
/// Entity Framework configuration for Rule entity.
/// </summary>
public class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.HasKey(r => r.Id);

        // Indexes
        builder.HasIndex(r => r.RulesetId);
        builder.HasIndex(r => r.Priority);
        builder.HasIndex(r => r.IsActive);

        // Property configuration
        builder.Property(r => r.Name).IsRequired().HasMaxLength(256);
        builder.Property(r => r.RuleText).IsRequired().HasMaxLength(2048);
        builder.Property(r => r.Description).HasMaxLength(1024);
        builder.Property(r => r.Priority).IsRequired();
        builder.Property(r => r.IsActive).IsRequired();

        // Ruleset relationship configured in RulesetConfiguration
    }
}
