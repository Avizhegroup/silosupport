using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Dtos;

/// <summary>Repository projection: how many forwarded messages a customer has per priority and resolution state.</summary>
public sealed record MessageCountRow(int CustomerId, GroupPriority Priority, bool IsResolved, int Count);

/// <summary>Repository projection: when a message was forwarded and with which priority.</summary>
public sealed record MessageTimeRow(DateTime CreatedAtUtc, GroupPriority Priority);

/// <summary>Messages forwarded with one priority (all time).</summary>
public sealed record PriorityCountDto(GroupPriority Priority, int Total, int Unresolved);

/// <summary>Messages forwarded for one customer, per priority.</summary>
public sealed record CustomerMessageStatsDto(int CustomerId, string CustomerName, int Low, int Medium, int Critical, int Unresolved)
{
    public int Total => Low + Medium + Critical;
}

/// <summary>Messages forwarded on one Iran-local day.</summary>
public sealed record DailyMessageCountDto(DateTime LocalDate, int Low, int Medium, int Critical)
{
    public int Total => Low + Medium + Critical;
}

/// <summary>Everything the dashboard (main page) shows.</summary>
public sealed record DashboardDto(
    int CustomerCount,
    int TotalMessages,
    int UnresolvedMessages,
    IReadOnlyList<PriorityCountDto> ByPriority,
    IReadOnlyList<CustomerMessageStatsDto> ByCustomer,
    IReadOnlyList<DailyMessageCountDto> Daily,
    int Days)
{
    public int ResolvedMessages => TotalMessages - UnresolvedMessages;
}
