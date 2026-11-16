using SiloSupport.Application.Dtos;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Interfaces;

/// <summary>Persistence port for issue threads (<see cref="SupportThread"/>). Threads are created together with their first message.</summary>
public interface ISupportThreadRepository
{
    /// <summary>
    /// <b>Every</b> open issue of the customer (at least one unresolved message), most recently active first - there is no
    /// age or count limit, because an issue stays open until somebody resolves it and the customer may come back to it
    /// at any time. Each issue carries its summary, message count, highest priority and its newest
    /// <paramref name="recentMessagesPerThread"/> customer texts (oldest first).
    /// </summary>
    Task<IReadOnlyList<ThreadContextDto>> GetOpenContextAsync(
        int customerId,
        int recentMessagesPerThread,
        CancellationToken cancellationToken = default);

    /// <summary>Replaces the summary of a thread (called after a follow-up message was added to it).</summary>
    Task UpdateSummaryAsync(int threadId, string summary, CancellationToken cancellationToken = default);

    /// <summary>All threads of the customer (without messages), most recently active first.</summary>
    Task<IReadOnlyList<SupportThread>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default);
}
