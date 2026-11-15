namespace SiloSupport.Service.CustomerSupport.Options;

/// <summary>
/// Settings of the issue tracking (section "Threads"): how much of a customer's history the AI sees when it decides
/// whether a new message continues an earlier issue.
/// </summary>
public class ThreadingOptions
{
    public const string SectionName = "Threads";

    /// <summary>Only issues with activity within this many hours are offered to the AI as "open issues".</summary>
    public int OpenWindowHours { get; set; } = 24;

    /// <summary>At most this many open issues are shown to the AI (the most recently active ones).</summary>
    public int MaxThreadsInContext { get; set; } = 5;

    /// <summary>At most this many of the newest messages of each issue are shown to the AI.</summary>
    public int MessagesPerThread { get; set; } = 4;
}
