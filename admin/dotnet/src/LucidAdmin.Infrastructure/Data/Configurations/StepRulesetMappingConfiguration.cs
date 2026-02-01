using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class StepRulesetMappingConfiguration : IEntityTypeConfiguration<StepRulesetMapping>
{
    public void Configure(EntityTypeBuilder<StepRulesetMapping> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.WorkflowStepId, e.RulesetId })
            .IsUnique();

        builder.HasOne(e => e.Ruleset)
            .WithMany()
            .HasForeignKey(e => e.RulesetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
