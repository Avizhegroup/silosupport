using SiloSupport.Application.Dtos;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Interfaces;

/// <summary>Persistence port for <see cref="Customer"/>. Reads are not tracked; writes detach what they touch.</summary>
public interface ICustomerRepository
{
    Task<IReadOnlyList<CustomerSummary>> GetSummariesAsync(CancellationToken cancellationToken = default);

    Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Inserts the customer; <see cref="Customer.Id"/> is populated afterwards.</summary>
    Task AddAsync(Customer customer, CancellationToken cancellationToken = default);

    /// <summary>Updates name and description. Returns false when the customer does not exist.</summary>
    Task<bool> UpdateAsync(Customer customer, CancellationToken cancellationToken = default);

    /// <summary>Deletes the customer. Returns false when it does not exist.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>True when any destination group of the customer has forwarded messages.</summary>
    Task<bool> HasMessageHistoryAsync(int customerId, CancellationToken cancellationToken = default);
}
