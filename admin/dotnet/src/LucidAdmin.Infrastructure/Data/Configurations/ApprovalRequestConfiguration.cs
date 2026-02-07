using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ApprovalRequestConfiguration : IEntityTypeConfiguration<ApprovalRequest>
{
    public void Configure(EntityTypeBuilder<ApprovalRequest> builder)
    {
        builder.HasKey(e => e.Id);

        // Indexes for common query patterns
        builder.HasIndex(e => e.Status);
        builder.HasIndex(e => new { e.AgentName, e.Status });
        builder.HasIndex(e => e.TicketId);

        // Property configuration
        builder.Property(e => e.WorkflowName).IsRequired().HasMaxLength(256);
        builder.Property(e => e.StepName).IsRequired().HasMaxLength(256);
        builder.Property(e => e.AgentName).IsRequired().HasMaxLength(256);
        builder.Property(e => e.TicketId).IsRequired().HasMaxLength(256);
        builder.Property(e => e.TicketShortDescription).HasMaxLength(512);
        builder.Property(e => e.ProposedAction).IsRequired().HasMaxLength(4000);
        builder.Property(e => e.ContextSnapshotJson).IsRequired(); // No max length — large JSON
        builder.Property(e => e.ResumeAfterStep).IsRequired().HasMaxLength(256);
        builder.Property(e => e.Decision).HasMaxLength(2000);
        builder.Property(e => e.DecidedBy).HasMaxLength(256);

        builder.Property(e => e.AutoApproveThreshold).HasPrecision(5, 4);
        builder.Property(e => e.Confidence).HasPrecision(5, 4);

        // WorkflowDefinition relationship (no navigation property on ApprovalRequest)
        // SetNull on delete - preserve approval history when workflow is deleted
        builder.HasOne<WorkflowDefinition>()
            .WithMany()
            .HasForeignKey(e => e.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
