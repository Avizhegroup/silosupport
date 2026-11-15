using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Dtos;

/// <summary>Metadata of the file sent with a message (the bytes are only loaded by <see cref="AttachmentContentDto"/>).</summary>
/// <param name="HasContent">False when the file could not be downloaded from Bale (it was still forwarded to the group).</param>
public sealed record AttachmentInfoDto(int Id, AttachmentKind Kind, string? FileName, string? MimeType, long? SizeBytes, bool HasContent);

/// <summary>The stored bytes of an attachment, served by the Web panel.</summary>
public sealed record AttachmentContentDto(int Id, AttachmentKind Kind, string? FileName, string? MimeType, byte[]? Content);

/// <summary>A forwarded message as shown on the conversations page.</summary>
/// <param name="ThreadId">The issue the message belongs to; null for messages recorded before threads existed.</param>
public sealed record GroupedMessageDto(
    int Id,
    int GroupId,
    string Message,
    DateTime CreatedAtUtc,
    bool IsResolved,
    int? ThreadId = null,
    AttachmentInfoDto? Attachment = null);

/// <summary>A message inside a thread, with the group it was forwarded to.</summary>
public sealed record ThreadMessageDto(
    int Id,
    int GroupId,
    string GroupName,
    GroupPriority Priority,
    string Message,
    DateTime CreatedAtUtc,
    bool IsResolved,
    AttachmentInfoDto? Attachment);

/// <summary>One issue of a customer: the messages that belong together (oldest first).</summary>
public sealed record ThreadDto(
    int Id,
    string Title,
    DateTime CreatedAtUtc,
    DateTime LastActivityUtc,
    IReadOnlyList<ThreadMessageDto> Messages)
{
    /// <summary>A thread is open while at least one of its messages is unresolved.</summary>
    public bool IsOpen => Messages.Any(m => !m.IsResolved);

    /// <summary>The highest priority among the messages of the thread.</summary>
    public GroupPriority HighestPriority => Messages.Count == 0 ? GroupPriority.Low : Messages.Max(m => m.Priority);
}

/// <summary>A recent open issue of a customer, given to the AI so it can tell follow-ups from new issues.</summary>
public sealed record ThreadContextDto(
    int ThreadId,
    string Title,
    DateTime LastActivityUtc,
    IReadOnlyList<ThreadContextMessageDto> Messages);

/// <summary>One earlier message of an open issue (what the customer wrote and the priority it got).</summary>
public sealed record ThreadContextMessageDto(string Text, GroupPriority Priority, DateTime CreatedAtUtc);

/// <summary>One destination group with its forwarded messages (oldest first).</summary>
public sealed record GroupConversationDto(
    int GroupId,
    string GroupName,
    GroupPriority Priority,
    IReadOnlyList<GroupedMessageDto> Messages);

/// <summary>All forwarded messages of one customer, grouped by destination group.</summary>
public sealed record CustomerConversationDto(
    int CustomerId,
    string CustomerName,
    IReadOnlyList<GroupConversationDto> Groups,
    IReadOnlyList<GroupPriority> MissingPriorities,
    IReadOnlyList<ThreadDto> Threads,
    IReadOnlyList<ThreadMessageDto> LegacyMessages);
