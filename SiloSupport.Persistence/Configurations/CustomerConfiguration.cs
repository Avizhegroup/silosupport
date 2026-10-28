using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Description).IsRequired().HasMaxLength(1000);

        // Members are plain configuration data and go away with the customer. Groups are cascaded too, but the
        // Restrict on CustomerGroupedMessage -> CustomerGroup stops the delete when message history exists.
        builder.HasMany(c => c.Members)
            .WithOne(m => m.Customer)
            .HasForeignKey(m => m.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(c => c.Groups)
            .WithOne(g => g.Customer)
            .HasForeignKey(g => g.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
