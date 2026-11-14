using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Configurations;

public class SupportPersonConfiguration : IEntityTypeConfiguration<SupportPerson>
{
    public void Configure(EntityTypeBuilder<SupportPerson> builder)
    {
        builder.ToTable("SupportPersons");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.PhoneNumber).IsRequired().HasMaxLength(32);
        builder.Property(s => s.BaleUsername).IsRequired().HasMaxLength(64);

        // One support person per Bale user name (stored normalized, so the comparison is case-insensitive).
        builder.HasIndex(s => s.BaleUsername).IsUnique();
    }
}
