using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Configurations;

public class SupportThreadConfiguration : IEntityTypeConfiguration<SupportThread>
{
    public void Configure(EntityTypeBuilder<SupportThread> builder)
    {
        builder.ToTable("SupportThreads");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Title).IsRequired().HasMaxLength(200);
        builder.Property(t => t.CreatedAtUtc).IsRequired();
        builder.Property(t => t.LastActivityUtc).IsRequired();

        // A customer without message history is deleted together with its (history-less) threads.
        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(t => t.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        // Messages keep existing when a thread disappears.
        builder.HasMany(t => t.Messages)
            .WithOne(m => m.Thread)
            .HasForeignKey(m => m.ThreadId)
            .OnDelete(DeleteBehavior.SetNull);

        // Finding a customer's most recently active issues.
        builder.HasIndex(t => new { t.CustomerId, t.LastActivityUtc });
    }
}
