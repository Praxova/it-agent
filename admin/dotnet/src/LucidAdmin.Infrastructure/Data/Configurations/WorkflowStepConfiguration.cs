using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class WorkflowStepConfiguration : IEntityTypeConfiguration<WorkflowStep>
{
    public void Configure(EntityTypeBuilder<WorkflowStep> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.DisplayName)
            .HasMaxLength(200);

        builder.Property(e => e.StepType)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.HasIndex(e => new { e.WorkflowDefinitionId, e.Name })
            .IsUnique();

        builder.HasMany(e => e.OutgoingTransitions)
            .WithOne(t => t.FromStep)
            .HasForeignKey(t => t.FromStepId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.IncomingTransitions)
            .WithOne(t => t.ToStep)
            .HasForeignKey(t => t.ToStepId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.RulesetMappings)
            .WithOne(m => m.WorkflowStep)
            .HasForeignKey(m => m.WorkflowStepId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
