namespace SiloSupport.Domain.Entities;

/// <summary>A message that was successfully forwarded to a customer's destination group.</summary>
public class CustomerGroupedMessage
{
    public int Id { get; set; }

    /// <summary>The exact text that was successfully sent to the destination Bale group.</summary>
    public string Message { get; set; } = string.Empty;

    public int GroupId { get; set; }

    public bool IsResolved { get; set; }

    /// <summary>UTC time the message was recorded (used to display messages chronologically).</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary>
    /// The text the customer wrote (or the caption of the file), without the header and analysis added when
    /// forwarding. Null for messages recorded before threads existed and for files sent without any text.
    /// </summary>
    public string? OriginalText { get; set; }

    /// <summary>The issue (conversation thread) the message belongs to; null for messages recorded before threads existed.</summary>
    public int? ThreadId { get; set; }

    public SupportThread? Thread { get; set; }

    /// <summary>The file that was sent with the message, if any.</summary>
    public MessageAttachment? Attachment { get; set; }

    public CustomerGroup? Group { get; set; }
}
