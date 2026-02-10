using LucidAdmin.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LucidAdmin.Infrastructure.Data.Configurations;

public class TicketCategoryConfiguration : IEntityTypeConfiguration<TicketCategory>
{
    public void Configure(EntityTypeBuilder<TicketCategory> builder)
    {
        builder.HasIndex(c => c.Name).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.DisplayName).HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(500);
        builder.Property(c => c.Color).HasMaxLength(20);
    }
}
