using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Interfaces;

/// <summary>Persistence port for <see cref="CustomerMember"/>.</summary>
public interface ICustomerMemberRepository
{
    Task<IReadOnlyList<CustomerMember>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task<CustomerMember?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Finds the member by Bale user id, with <see cref="CustomerMember.Customer"/> loaded.</summary>
    Task<CustomerMember?> FindByBaleIdAsync(long baleId, CancellationToken cancellationToken = default);

    /// <summary>True when another member (ignoring <paramref name="excludeMemberId"/>) already uses the Bale id.</summary>
    Task<bool> BaleIdExistsAsync(long baleId, int? excludeMemberId, CancellationToken cancellationToken = default);

    Task AddAsync(CustomerMember member, CancellationToken cancellationToken = default);

    /// <summary>Updates name, description, phone and Bale id. The owning customer never changes.</summary>
    Task<bool> UpdateAsync(CustomerMember member, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
