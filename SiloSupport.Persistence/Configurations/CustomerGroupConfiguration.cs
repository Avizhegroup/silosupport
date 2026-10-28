using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Configurations;

public class CustomerGroupConfiguration : IEntityTypeConfiguration<CustomerGroup>
{
    public void Configure(EntityTypeBuilder<CustomerGroup> builder)
    {
        builder.ToTable("CustomerGroups");
        builder.HasKey(g => g.Id);

        builder.Property(g => g.Name).IsRequired().HasMaxLength(200);
        builder.Property(g => g.BaleId).IsRequired();
        builder.Property(g => g.CustomerId).IsRequired();

        // Stored as its name (Low/Medium/Critical) so the table stays readable.
        builder.Property(g => g.Priority).IsRequired().HasConversion<string>().HasMaxLength(16);

        // One destination group per priority and customer ...
        builder.HasIndex(g => new { g.CustomerId, g.Priority }).IsUnique();

        // ... and a Bale chat can be the destination of only one group.
        builder.HasIndex(g => g.BaleId).IsUnique();

        // Never cascade-delete message history.
        builder.HasMany(g => g.Messages)
            .WithOne(m => m.Group)
            .HasForeignKey(m => m.GroupId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
