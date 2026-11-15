using SiloSupport.Domain.Enums;

namespace SiloSupport.Domain.Entities;

/// <summary>
/// A file (photo, video, voice message, audio, document, animation) that a customer sent and that was forwarded to a
/// priority group together with its message. The bytes are kept in the database so both the Windows service and the
/// Web panel can reach them; <see cref="Content"/> is null when the file could not be downloaded (for example it is
/// larger than the Bale bot download limit of 20 MB) - it was still forwarded to the group by its Bale file id.
/// </summary>
public class MessageAttachment
{
    public int Id { get; set; }

    public int GroupedMessageId { get; set; }

    public AttachmentKind Kind { get; set; }

    public string? FileName { get; set; }

    public string? MimeType { get; set; }

    /// <summary>Size in bytes as reported by Bale (or the downloaded length); null when unknown.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>Bale file id (usable by this bot to send the file again).</summary>
    public string BaleFileId { get; set; } = string.Empty;

    public string? BaleFileUniqueId { get; set; }

    /// <summary>The downloaded bytes; null when the download was not possible.</summary>
    public byte[]? Content { get; set; }

    public CustomerGroupedMessage? GroupedMessage { get; set; }
}
