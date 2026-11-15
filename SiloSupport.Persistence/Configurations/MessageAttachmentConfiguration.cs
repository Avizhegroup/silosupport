using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Persistence.Configurations;

public class MessageAttachmentConfiguration : IEntityTypeConfiguration<MessageAttachment>
{
    public void Configure(EntityTypeBuilder<MessageAttachment> builder)
    {
        builder.ToTable("MessageAttachments");
        builder.HasKey(a => a.Id);

        // Stored as its name (Photo/Video/...) so the table stays readable.
        builder.Property(a => a.Kind).IsRequired().HasConversion<string>().HasMaxLength(16);
        builder.Property(a => a.FileName).HasMaxLength(260);
        builder.Property(a => a.MimeType).HasMaxLength(128);
        builder.Property(a => a.BaleFileId).IsRequired().HasMaxLength(512);
        builder.Property(a => a.BaleFileUniqueId).HasMaxLength(128);
        builder.Property(a => a.Content); // varbinary(max) on SQL Server, BLOB on SQLite

        // One file per message; it disappears with its message.
        builder.HasOne(a => a.GroupedMessage)
            .WithOne(m => m.Attachment)
            .HasForeignKey<MessageAttachment>(a => a.GroupedMessageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.GroupedMessageId).IsUnique();
    }
}
