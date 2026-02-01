using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class WorkflowDefinitionConfiguration : IEntityTypeConfiguration<WorkflowDefinition>
{
    public void Configure(EntityTypeBuilder<WorkflowDefinition> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(e => e.Name)
            .IsUnique();

        builder.Property(e => e.DisplayName)
            .HasMaxLength(200);

        builder.Property(e => e.Description)
            .HasMaxLength(2000);

        builder.Property(e => e.Version)
            .HasMaxLength(20);

        builder.HasOne(e => e.ExampleSet)
            .WithMany()
            .HasForeignKey(e => e.ExampleSetId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Steps)
            .WithOne(s => s.WorkflowDefinition)
            .HasForeignKey(s => s.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.RulesetMappings)
            .WithOne(m => m.WorkflowDefinition)
            .HasForeignKey(m => m.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
