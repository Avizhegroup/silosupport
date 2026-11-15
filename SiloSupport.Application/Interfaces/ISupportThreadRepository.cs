using SiloSupport.Application.Dtos;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Interfaces;

/// <summary>Persistence port for issue threads (<see cref="SupportThread"/>). Threads are created together with their first message.</summary>
public interface ISupportThreadRepository
{
    /// <summary>
    /// The customer's open issues for the AI: threads with an unresolved message and activity at or after
    /// <paramref name="sinceUtc"/>, most recent first (at most <paramref name="maxThreads"/>), each with its newest
    /// <paramref name="messagesPerThread"/> messages oldest first. Only messages with a recorded customer text are included.
    /// </summary>
    Task<IReadOnlyList<ThreadContextDto>> GetOpenContextAsync(
        int customerId,
        DateTime sinceUtc,
        int maxThreads,
        int messagesPerThread,
        CancellationToken cancellationToken = default);

    /// <summary>All threads of the customer (without messages), most recently active first.</summary>
    Task<IReadOnlyList<SupportThread>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default);
}
