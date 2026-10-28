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

    public CustomerGroup? Group { get; set; }
}
