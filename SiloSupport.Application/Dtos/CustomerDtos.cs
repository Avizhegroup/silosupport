using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Dtos;

/// <summary>Repository projection used to build the customer list efficiently (one query).</summary>
public sealed record CustomerSummary(
    int Id,
    string Name,
    string Description,
    int MemberCount,
    IReadOnlyList<GroupPriority> ConfiguredPriorities);

public sealed record CustomerDto(int Id, string Name, string Description, long? BaleChatId = null);

/// <summary>Row of the customer list, including which priority groups are still missing.</summary>
public sealed record CustomerListItemDto(
    int Id,
    string Name,
    string Description,
    int MemberCount,
    int GroupCount,
    IReadOnlyList<GroupPriority> MissingPriorities);

/// <param name="BaleId">Optional Bale user name or numeric id entered by an administrator.</param>
public sealed record MemberDto(int Id, int CustomerId, string Name, string Description, string PhoneNumber, string? BaleId);

/// <param name="BaleUsername">Normalized Bale user name (no '@', lower case).</param>
public sealed record SupportPersonDto(int Id, string Name, string PhoneNumber, string BaleUsername);

public sealed record GroupDto(int Id, int CustomerId, string Name, long BaleId, GroupPriority Priority);

/// <summary>Configuration state of one priority for a customer: the group, or null when it is missing.</summary>
public sealed record PriorityGroupStatusDto(GroupPriority Priority, GroupDto? Group)
{
    public bool IsConfigured => Group is not null;
}

public sealed record CustomerDetailsDto(
    CustomerDto Customer,
    IReadOnlyList<MemberDto> Members,
    IReadOnlyList<PriorityGroupStatusDto> PriorityGroups)
{
    public IReadOnlyList<GroupPriority> MissingPriorities =>
        PriorityGroups.Where(p => !p.IsConfigured).Select(p => p.Priority).ToList();
}
