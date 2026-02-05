using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class AgentServiceAccountBindingConfiguration
    : IEntityTypeConfiguration<AgentServiceAccountBinding>
{
    public void Configure(EntityTypeBuilder<AgentServiceAccountBinding> builder)
    {
        builder.ToTable("AgentServiceAccountBindings");

        builder.HasOne(b => b.Agent)
            .WithMany(a => a.ServiceAccountBindings)
            .HasForeignKey(b => b.AgentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.ServiceAccount)
            .WithMany()
            .HasForeignKey(b => b.ServiceAccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(b => b.Role)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(b => b.Qualifier)
            .HasMaxLength(100);

        // Unique: one role+qualifier per agent
        builder.HasIndex(b => new { b.AgentId, b.Role, b.Qualifier })
            .IsUnique();
    }
}
