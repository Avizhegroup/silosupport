using SiloSupport.Application.Dtos;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Interfaces;

/// <summary>Persistence port for forwarded-message history.</summary>
public interface ICustomerGroupedMessageRepository
{
    Task AddAsync(CustomerGroupedMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads only the messages of the given customer (filtered in the database), oldest first.
    /// </summary>
    Task<IReadOnlyList<GroupedMessageDto>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets the resolution flag of a message that belongs to the given customer.
    /// Returns false when no such message exists for that customer.
    /// </summary>
    Task<bool> SetResolvedAsync(int customerId, int messageId, bool isResolved, CancellationToken cancellationToken = default);
}
