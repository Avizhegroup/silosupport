using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Configurations;

public class CustomerGroupedMessageConfiguration : IEntityTypeConfiguration<CustomerGroupedMessage>
{
    public void Configure(EntityTypeBuilder<CustomerGroupedMessage> builder)
    {
        builder.ToTable("CustomerGroupedMessages");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Message).IsRequired().HasMaxLength(4096);
        builder.Property(m => m.GroupId).IsRequired();
        builder.Property(m => m.IsResolved).IsRequired().HasDefaultValue(false);
        builder.Property(m => m.CreatedAtUtc).IsRequired();

        // Loading a group's history chronologically.
        builder.HasIndex(m => new { m.GroupId, m.CreatedAtUtc });
    }
}
