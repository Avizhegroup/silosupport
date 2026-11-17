namespace SiloSupport.Domain.Entities;

/// <summary>
/// One issue of a customer that may be described over several messages. A customer's messages that belong together
/// share a thread; unrelated messages start their own thread. A thread is "open" while it has an unresolved message, and
/// there is no time or count limit: a message that returns to a subject that is still open joins that thread, however old it is.
/// </summary>
public class SupportThread
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    /// <summary>Short title of the issue (given by the AI for the first message, or an excerpt of it).</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// What the issue is about so far, in one or two sentences. The AI rewrites it with every new message of the thread
    /// (it always covers the whole issue, not just the last message), and it is what the AI reads about older issues.
    /// </summary>
    public string? Summary { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>UTC time of the newest message of the thread.</summary>
    public DateTime LastActivityUtc { get; set; }

    public List<CustomerGroupedMessage> Messages { get; set; } = new();
}
