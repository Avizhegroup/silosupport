using SiloSupport.Domain.Enums;

namespace SiloSupport.Service.CustomerSupport.Models;

/// <summary>Result of asking the AI API for a priority.</summary>
public sealed class PriorityClassificationResult
{
    private PriorityClassificationResult(bool succeeded, GroupPriority priority, string? analysis, string? error)
    {
        Succeeded = succeeded;
        Priority = priority;
        Analysis = analysis;
        Error = error;
    }

    public bool Succeeded { get; }

    public GroupPriority Priority { get; }

    /// <summary>The analysis text actually returned by the AI; null when the API returned none.</summary>
    public string? Analysis { get; }

    public string? Error { get; }

    public static PriorityClassificationResult Success(GroupPriority priority, string? analysis) =>
        new(true, priority, analysis, null);

    public static PriorityClassificationResult Failure(string error) =>
        new(false, default, null, error);
}

/// <summary>Everything needed to build the text forwarded to a priority group.</summary>
public sealed record ForwardedMessageContext(
    string CustomerName,
    string MemberName,
    string MemberPhoneNumber,
    GroupPriority Priority,
    string OriginalMessage,
    string? Analysis);

/// <summary>Result of processing one incoming Bale message (useful for tests and diagnostics).</summary>
public enum MessageProcessingOutcome
{
    Forwarded,
    IgnoredPriorityGroup,
    IgnoredBotSender,
    IgnoredUnsupportedMessage,
    IgnoredEmptyText,
    IgnoredUnknownMember,
    ClassificationFailed,
    DestinationGroupMissing,
    SendFailed,
    PersistenceFailed
}
