using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Dtos;

/// <summary>A forwarded message as shown on the conversations page.</summary>
public sealed record GroupedMessageDto(int Id, int GroupId, string Message, DateTime CreatedAtUtc, bool IsResolved);

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
    IReadOnlyList<GroupPriority> MissingPriorities);
