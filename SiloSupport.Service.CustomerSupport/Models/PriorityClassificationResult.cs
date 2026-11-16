using SiloSupport.Domain.Enums;

namespace SiloSupport.Service.CustomerSupport.Models;

/// <summary>Result of asking the AI API for a priority.</summary>
public sealed class PriorityClassificationResult
{
    private PriorityClassificationResult(bool succeeded, GroupPriority priority, string? analysis, string? error, int? threadId, string? threadTitle, string? threadSummary)
    {
        Succeeded = succeeded;
        Priority = priority;
        Analysis = analysis;
        Error = error;
        ThreadId = threadId;
        ThreadSummary = threadSummary;
        ThreadTitle = threadTitle;
    }

    public bool Succeeded { get; }

    public GroupPriority Priority { get; }

    /// <summary>The analysis text actually returned by the AI; null when the API returned none.</summary>
    public string? Analysis { get; }

    public string? Error { get; }

    /// <summary>
    /// The open issue the AI says the message continues. Null when the message starts a new issue. The processor only
    /// accepts an id that was offered to the AI.
    /// </summary>
    public int? ThreadId { get; }

    /// <summary>The AI's up-to-date summary of the whole issue the message belongs to (including this message); null when it gave none.</summary>
    public string? ThreadSummary { get; }

    /// <summary>A short title of the issue the message belongs to (used as the title of a new issue); null when the AI gave none.</summary>
    public string? ThreadTitle { get; }

    public static PriorityClassificationResult Success(GroupPriority priority, string? analysis, int? threadId = null, string? threadTitle = null, string? threadSummary = null) =>
        new(true, priority, analysis, null, threadId, threadTitle, threadSummary);

    public static PriorityClassificationResult Failure(string error) =>
        new(false, default, null, error, null, null, null);
}

/// <summary>Everything needed to build the text forwarded to a priority group.</summary>
public sealed record ForwardedMessageContext(
    string CustomerName,
    string MemberName,
    string MemberPhoneNumber,
    GroupPriority Priority,
    string OriginalMessage,
    string? Analysis,
    string? ThreadTitle = null,
    bool IsFollowUp = false,
    AttachmentKind? AttachmentKind = null,
    string? AttachmentFileName = null);

/// <summary>Result of processing one incoming Bale message (useful for tests and diagnostics).</summary>
public enum MessageProcessingOutcome
{
    Forwarded,
    IgnoredPriorityGroup,
    IgnoredBotSender,
    IgnoredSupportPerson,
    IgnoredUnsupportedMessage,
    IgnoredEmptyText,
    IgnoredUnknownMember,
    ClassificationFailed,
    DestinationGroupMissing,
    SendFailed,
    PersistenceFailed
}
