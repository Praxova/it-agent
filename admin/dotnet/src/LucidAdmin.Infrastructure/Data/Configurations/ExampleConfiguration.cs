using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ExampleConfiguration : IEntityTypeConfiguration<Example>
{
    public void Configure(EntityTypeBuilder<Example> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(e => e.TicketShortDescription)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.TicketDescription)
            .HasMaxLength(4000);

        builder.Property(e => e.CallerName)
            .HasMaxLength(200);

        builder.HasOne(e => e.TicketCategory)
            .WithMany(c => c.Examples)
            .HasForeignKey(e => e.TicketCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(e => e.ExpectedConfidence)
            .HasPrecision(3, 2);

        builder.Property(e => e.ExpectedAffectedUser)
            .HasMaxLength(200);

        builder.Property(e => e.ExpectedTargetGroup)
            .HasMaxLength(200);

        builder.Property(e => e.ExpectedTargetResource)
            .HasMaxLength(500);

        builder.Property(e => e.ExpectedPermissionLevel)
            .HasMaxLength(50);

        builder.Property(e => e.ExpectedEscalationReason)
            .HasMaxLength(500);

        builder.Property(e => e.Notes)
            .HasMaxLength(1000);

        builder.HasIndex(e => new { e.ExampleSetId, e.Name })
            .IsUnique();
    }
}
