using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;

namespace SiloSupport.Application.Services;

/// <summary>Use cases for managing a customer's destination priority groups (one per priority).</summary>
public class CustomerGroupService
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerGroupRepository _groups;
    private readonly ITransactionRunner _transactions;

    public CustomerGroupService(
        ICustomerRepository customers,
        ICustomerGroupRepository groups,
        ITransactionRunner transactions)
    {
        _customers = customers;
        _groups = groups;
        _transactions = transactions;
    }

    public async Task<IReadOnlyList<GroupDto>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var groups = await _groups.GetByCustomerAsync(customerId, cancellationToken);
        return groups.Select(ToDto).ToList();
    }

    public async Task<GroupDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var group = await _groups.GetByIdAsync(id, cancellationToken);
        return group is null ? null : ToDto(group);
    }

    /// <summary>One entry per supported priority, so missing configuration is visible to administrators.</summary>
    public async Task<IReadOnlyList<PriorityGroupStatusDto>> GetPriorityStatusAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var groups = await _groups.GetByCustomerAsync(customerId, cancellationToken);

        return PriorityHelper.Ordered
            .Select(priority =>
            {
                var group = groups.FirstOrDefault(g => g.Priority == priority);
                return new PriorityGroupStatusDto(priority, group is null ? null : ToDto(group));
            })
            .ToList();
    }

    public async Task<OperationResult<int>> CreateAsync(int customerId, GroupInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidation.Validate(input);
        if (errors.Count > 0)
            return OperationResult<int>.Failure(errors);

        return await _transactions.ExecuteAsync(async () =>
        {
            if (await _customers.GetByIdAsync(customerId, cancellationToken) is null)
                return OperationResult<int>.Failure("The customer no longer exists.");

            var conflict = await FindConflictAsync(customerId, input, null, cancellationToken);
            if (conflict is not null)
                return OperationResult<int>.Failure(conflict);

            var group = new CustomerGroup
            {
                CustomerId = customerId,
                Name = input.Name.Trim(),
                BaleId = input.BaleId,
                Priority = input.Priority
            };

            await _groups.AddAsync(group, cancellationToken);
            return OperationResult<int>.Success(group.Id);
        });
    }

    public async Task<OperationResult> UpdateAsync(int id, GroupInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidation.Validate(input);
        if (errors.Count > 0)
            return OperationResult.Failure(errors);

        return await _transactions.ExecuteAsync(async () =>
        {
            var existing = await _groups.GetByIdAsync(id, cancellationToken);
            if (existing is null)
                return OperationResult.Failure("The group no longer exists.");

            var conflict = await FindConflictAsync(existing.CustomerId, input, id, cancellationToken);
            if (conflict is not null)
                return OperationResult.Failure(conflict);

            var updated = await _groups.UpdateAsync(
                new CustomerGroup { Id = id, Name = input.Name.Trim(), BaleId = input.BaleId, Priority = input.Priority },
                cancellationToken);

            return updated ? OperationResult.Success() : OperationResult.Failure("The group no longer exists.");
        });
    }

    /// <summary>A group that already received forwarded messages cannot be deleted (history is preserved).</summary>
    public async Task<OperationResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _transactions.ExecuteAsync(async () =>
        {
            if (await _groups.GetByIdAsync(id, cancellationToken) is null)
                return OperationResult.Failure("The group no longer exists.");

            if (await _groups.HasMessagesAsync(id, cancellationToken))
                return OperationResult.Failure("This group has forwarded-message history and cannot be deleted.");

            await _groups.DeleteAsync(id, cancellationToken);
            return OperationResult.Success();
        });
    }

    private async Task<string?> FindConflictAsync(int customerId, GroupInput input, int? excludeGroupId, CancellationToken cancellationToken)
    {
        if (await _groups.PriorityTakenAsync(customerId, input.Priority, excludeGroupId, cancellationToken))
            return $"This customer already has a {input.Priority} priority group.";

        if (await _groups.BaleIdExistsAsync(input.BaleId, excludeGroupId, cancellationToken))
            return "Another priority group already uses this Bale group ID.";

        return null;
    }

    private static GroupDto ToDto(CustomerGroup g) => new(g.Id, g.CustomerId, g.Name, g.BaleId, g.Priority);
}
