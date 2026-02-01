using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class StepTransitionConfiguration : IEntityTypeConfiguration<StepTransition>
{
    public void Configure(EntityTypeBuilder<StepTransition> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Label)
            .HasMaxLength(100);

        builder.Property(e => e.Condition)
            .HasMaxLength(500);

        builder.HasIndex(e => new { e.FromStepId, e.ToStepId, e.OutputIndex })
            .IsUnique();
    }
}
