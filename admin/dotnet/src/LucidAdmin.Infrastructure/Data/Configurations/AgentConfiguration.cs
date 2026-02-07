using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

/// <summary>
/// Entity Framework configuration for Agent entity.
/// </summary>
public class AgentConfiguration : IEntityTypeConfiguration<Agent>
{
    public void Configure(EntityTypeBuilder<Agent> builder)
    {
        builder.HasKey(a => a.Id);

        // Indexes
        builder.HasIndex(a => a.Name).IsUnique();
        builder.HasIndex(a => a.Status);
        builder.HasIndex(a => a.HostName);

        // Property configuration
        builder.Property(a => a.Name).IsRequired().HasMaxLength(256);
        builder.Property(a => a.DisplayName).HasMaxLength(256);
        builder.Property(a => a.Description).HasMaxLength(1024);
        builder.Property(a => a.HostName).HasMaxLength(256);  // Now nullable
        builder.Property(a => a.AssignmentGroup).HasMaxLength(256);

        // LLM ServiceAccount relationship
        // SetNull on delete - if the LLM ServiceAccount is deleted, just nullify the reference
        builder.HasOne(a => a.LlmServiceAccount)
            .WithMany()  // ServiceAccount doesn't need navigation back to agents
            .HasForeignKey(a => a.LlmServiceAccountId)
            .OnDelete(DeleteBehavior.SetNull);

        // ServiceNow ServiceAccount relationship
        // SetNull on delete - if the ServiceNow account is deleted, just nullify the reference
        builder.HasOne(a => a.ServiceNowAccount)
            .WithMany()  // ServiceAccount doesn't need navigation back to agents
            .HasForeignKey(a => a.ServiceNowAccountId)
            .OnDelete(DeleteBehavior.SetNull);

        // WorkflowDefinition relationship
        // SetNull on delete - if the workflow is deleted, just nullify the reference
        builder.HasOne(a => a.WorkflowDefinition)
            .WithMany()
            .HasForeignKey(a => a.WorkflowDefinitionId)
            .OnDelete(DeleteBehavior.SetNull);

        // AuditEvents relationship is configured in AuditEventConfiguration
    }
}
