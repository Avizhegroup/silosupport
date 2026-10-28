using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Configurations;

public class CustomerMemberConfiguration : IEntityTypeConfiguration<CustomerMember>
{
    public void Configure(EntityTypeBuilder<CustomerMember> builder)
    {
        builder.ToTable("CustomerMembers");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Name).IsRequired().HasMaxLength(200);
        builder.Property(m => m.Description).IsRequired().HasMaxLength(1000);
        builder.Property(m => m.PhoneNumber).IsRequired().HasMaxLength(32);
        builder.Property(m => m.BaleId).IsRequired();
        builder.Property(m => m.CustomerId).IsRequired();

        builder.HasIndex(m => m.CustomerId);

        // A Bale user identifies exactly one member, otherwise messages could not be attributed to one customer.
        builder.HasIndex(m => m.BaleId).IsUnique();
    }
}
