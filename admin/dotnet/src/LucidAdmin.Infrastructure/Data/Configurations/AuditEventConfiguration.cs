using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class AuditEventConfiguration : IEntityTypeConfiguration<AuditEvent>
{
    public void Configure(EntityTypeBuilder<AuditEvent> builder)
    {
        builder.HasKey(ae => ae.Id);
        builder.HasIndex(ae => ae.CreatedAt);
        builder.HasIndex(ae => ae.Action);
        builder.HasIndex(ae => ae.ToolServerId);
        builder.HasIndex(ae => ae.AgentId);
        builder.HasIndex(ae => ae.CapabilityId);

        builder.Property(ae => ae.CapabilityId).HasMaxLength(100);
        builder.Property(ae => ae.PerformedBy).HasMaxLength(256);
        builder.Property(ae => ae.TargetResource).HasMaxLength(512);
        builder.Property(ae => ae.TicketNumber).HasMaxLength(64);
        builder.Property(ae => ae.ErrorMessage).HasMaxLength(2048);
        builder.Property(ae => ae.DetailsJson).HasMaxLength(8192);

        builder.HasOne(ae => ae.ToolServer)
            .WithMany(ts => ts.AuditEvents)
            .HasForeignKey(ae => ae.ToolServerId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(ae => ae.Agent)
            .WithMany(a => a.AuditEvents)
            .HasForeignKey(ae => ae.AgentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
