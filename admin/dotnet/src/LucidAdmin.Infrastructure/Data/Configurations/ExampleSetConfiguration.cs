using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using LucidAdmin.Core.Entities;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class ExampleSetConfiguration : IEntityTypeConfiguration<ExampleSet>
{
    public void Configure(EntityTypeBuilder<ExampleSet> builder)
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
            .HasMaxLength(1000);

        builder.HasOne(e => e.TicketCategory)
            .WithMany(c => c.ExampleSets)
            .HasForeignKey(e => e.TicketCategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(e => e.Examples)
            .WithOne(ex => ex.ExampleSet)
            .HasForeignKey(ex => ex.ExampleSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
