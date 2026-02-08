using LucidAdmin.Core.Entities;
using LucidAdmin.Core.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ClarificationConfiguration : IEntityTypeConfiguration<Clarification>
{
    public void Configure(EntityTypeBuilder<Clarification> builder)
    {
        builder.ToTable("Clarifications");

        builder.HasKey(e => e.Id);

        // Indexes for common query patterns
        builder.HasIndex(e => new { e.AgentName, e.Status });
        builder.HasIndex(e => e.TicketId);

        // Property configuration
        builder.Property(e => e.AgentName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.WorkflowName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.StepName).IsRequired().HasMaxLength(200);
        builder.Property(e => e.TicketId).IsRequired().HasMaxLength(100);
        builder.Property(e => e.TicketSysId).HasMaxLength(100);
        builder.Property(e => e.Question).IsRequired().HasMaxLength(4000);

        builder.Property(e => e.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .HasDefaultValue(ClarificationStatus.Pending);

        builder.Property(e => e.ContextSnapshotJson).IsRequired();
        builder.Property(e => e.ResumeAfterStep).HasMaxLength(200);
        builder.Property(e => e.UserReply).HasMaxLength(4000);
    }
}
