using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Interfaces;

/// <summary>Persistence port for destination <see cref="CustomerGroup"/> records.</summary>
public interface ICustomerGroupRepository
{
    Task<IReadOnlyList<CustomerGroup>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default);

    Task<CustomerGroup?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Resolves the destination group of exactly this customer and priority (never another customer's).</summary>
    Task<CustomerGroup?> GetByCustomerAndPriorityAsync(int customerId, GroupPriority priority, CancellationToken cancellationToken = default);

    /// <summary>True when any configured destination group (ignoring <paramref name="excludeGroupId"/>) has this Bale chat id.</summary>
    Task<bool> BaleIdExistsAsync(long baleId, int? excludeGroupId, CancellationToken cancellationToken = default);

    /// <summary>True when the customer already has a group for the priority (ignoring <paramref name="excludeGroupId"/>).</summary>
    Task<bool> PriorityTakenAsync(int customerId, GroupPriority priority, int? excludeGroupId, CancellationToken cancellationToken = default);

    Task<bool> HasMessagesAsync(int groupId, CancellationToken cancellationToken = default);

    Task AddAsync(CustomerGroup group, CancellationToken cancellationToken = default);

    /// <summary>Updates name, Bale id and priority. The owning customer never changes.</summary>
    Task<bool> UpdateAsync(CustomerGroup group, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
