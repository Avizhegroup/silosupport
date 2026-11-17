namespace SiloSupport.Service.CustomerSupport.Options;

/// <summary>
/// Settings of the issue tracking (section "Threads"). There is deliberately <b>no</b> age or count limit on which of a
/// customer's open issues the AI may link a message to: an issue stays open until it is resolved, and the customer may
/// come back to it after days. These two values only bound the size of the request sent to the AI.
/// </summary>
public class ThreadingOptions
{
    public const string SectionName = "Threads";

    /// <summary>
    /// How many of the newest customer messages of each open issue are written into the request, in addition to the
    /// issue's summary (which covers all older messages).
    /// </summary>
    public int MessagesPerThread { get; set; } = 3;

    /// <summary>
    /// Technical guard for the size of the request: when the description of all open issues would exceed this many
    /// characters, the issues with the oldest activity are left out of the request (and a warning is logged). The default
    /// is far above what a normal customer has, so in practice every open issue is offered.
    /// </summary>
    public int MaxContextCharacters { get; set; } = 60000;
}
