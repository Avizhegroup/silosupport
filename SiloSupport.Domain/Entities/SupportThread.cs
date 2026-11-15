namespace SiloSupport.Domain.Entities;

/// <summary>
/// One issue of a customer that may be described over several messages. A customer's messages that belong together
/// share a thread; unrelated messages start their own thread. A thread is "open" while it has an unresolved message.
/// </summary>
public class SupportThread
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    /// <summary>Short title of the issue (given by the AI for the first message, or an excerpt of it).</summary>
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC time of the newest message of the thread.</summary>
    public DateTime LastActivityUtc { get; set; }

    public List<CustomerGroupedMessage> Messages { get; set; } = new();
}
