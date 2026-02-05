using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ManualSubmissionConfiguration : IEntityTypeConfiguration<ManualSubmission>
{
    public void Configure(EntityTypeBuilder<ManualSubmission> builder)
    {
        builder.HasKey(e => e.Id);

        // Indexes
        builder.HasIndex(e => new { e.AgentId, e.Status });
        builder.HasIndex(e => e.SubmittedAt);

        // Property configuration
        builder.Property(e => e.Title).IsRequired().HasMaxLength(512);
        builder.Property(e => e.Description).HasMaxLength(4000);
        builder.Property(e => e.Requester).HasMaxLength(256);
        builder.Property(e => e.ResultStatus).HasMaxLength(50);
        builder.Property(e => e.ResultMessage).HasMaxLength(4000);

        // Relationships
        builder.HasOne(e => e.Agent)
            .WithMany()
            .HasForeignKey(e => e.AgentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
