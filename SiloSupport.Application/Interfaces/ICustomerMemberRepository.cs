using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Interfaces;

/// <summary>Persistence port for <see cref="CustomerMember"/>.</summary>
public interface ICustomerMemberRepository
{
    Task<IReadOnlyList<CustomerMember>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task<CustomerMember?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds the members a Bale sender may be: the members whose stored Bale ID equals the numeric id or the user name
    /// (ignoring case and a leading '@'; see <see cref="SiloSupport.Application.Helpers.BaleIdentity.CandidatesFor"/>).
    /// <see cref="CustomerMember.Customer"/> is loaded.
    /// </summary>
    Task<IReadOnlyList<CustomerMember>> FindBySenderAsync(long? senderId, string? username, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when another member (ignoring <paramref name="excludeMemberId"/>) already uses the Bale ID, ignoring
    /// case and a leading '@' ("@Ali" and "ali" are the same ID).
    /// </summary>
    Task<bool> BaleIdExistsAsync(string baleId, int? excludeMemberId, CancellationToken cancellationToken = default);

    Task AddAsync(CustomerMember member, CancellationToken cancellationToken = default);

    /// <summary>Updates name, description, phone and Bale ID. The owning customer never changes.</summary>
    Task<bool> UpdateAsync(CustomerMember member, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
