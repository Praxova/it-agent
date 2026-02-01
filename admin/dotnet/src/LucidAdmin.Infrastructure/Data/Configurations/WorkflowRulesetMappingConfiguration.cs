using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class WorkflowRulesetMappingConfiguration : IEntityTypeConfiguration<WorkflowRulesetMapping>
{
    public void Configure(EntityTypeBuilder<WorkflowRulesetMapping> builder)
    {
        builder.HasKey(e => e.Id);

        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.RulesetId })
            .IsUnique();

        builder.HasOne(e => e.Ruleset)
            .WithMany()
            .HasForeignKey(e => e.RulesetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
