using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Application.Services;

/// <summary>Use cases for managing customers.</summary>
public class CustomerService
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerMemberRepository _members;
    private readonly ICustomerGroupRepository _groups;
    private readonly ITransactionRunner _transactions;

    public CustomerService(
        ICustomerRepository customers,
        ICustomerMemberRepository members,
        ICustomerGroupRepository groups,
        ITransactionRunner transactions)
    {
        _customers = customers;
        _members = members;
        _groups = groups;
        _transactions = transactions;
    }

    public async Task<IReadOnlyList<CustomerListItemDto>> GetListAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await _customers.GetSummariesAsync(cancellationToken);

        return summaries
            .Select(s => new CustomerListItemDto(
                s.Id,
                s.Name,
                s.Description,
                s.MemberCount,
                s.ConfiguredPriorities.Count,
                PriorityHelper.Missing(s.ConfiguredPriorities)))
            .ToList();
    }

    public async Task<CustomerDto?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(id, cancellationToken);
        return customer is null ? null : new CustomerDto(customer.Id, customer.Name, customer.Description);
    }

    /// <summary>Customer with its members and the configuration state of each priority group.</summary>
    public async Task<CustomerDetailsDto?> GetDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(id, cancellationToken);
        if (customer is null)
            return null;

        var members = await _members.GetByCustomerAsync(id, cancellationToken);
        var groups = await _groups.GetByCustomerAsync(id, cancellationToken);

        var statuses = PriorityHelper.Ordered
            .Select(priority =>
            {
                var group = groups.FirstOrDefault(g => g.Priority == priority);
                return new PriorityGroupStatusDto(
                    priority,
                    group is null ? null : new GroupDto(group.Id, group.CustomerId, group.Name, group.BaleId, group.Priority));
            })
            .ToList();

        return new CustomerDetailsDto(
            new CustomerDto(customer.Id, customer.Name, customer.Description),
            members.Select(m => new MemberDto(m.Id, m.CustomerId, m.Name, m.Description, m.PhoneNumber, m.BaleId)).ToList(),
            statuses);
    }

    public async Task<OperationResult<int>> CreateAsync(CustomerInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidation.Validate(input);
        if (errors.Count > 0)
            return OperationResult<int>.Failure(errors);

        var customer = new Customer { Name = input.Name.Trim(), Description = input.Description?.Trim() ?? string.Empty };
        await _customers.AddAsync(customer, cancellationToken);
        return OperationResult<int>.Success(customer.Id);
    }

    public async Task<OperationResult> UpdateAsync(int id, CustomerInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidation.Validate(input);
        if (errors.Count > 0)
            return OperationResult.Failure(errors);

        var updated = await _customers.UpdateAsync(
            new Customer { Id = id, Name = input.Name.Trim(), Description = input.Description?.Trim() ?? string.Empty },
            cancellationToken);

        return updated ? OperationResult.Success() : OperationResult.Failure("مشتری دیگر وجود ندارد.");
    }

    /// <summary>
    /// Deletes a customer only when that does not destroy forwarded-message history. The check and the delete
    /// run in one transaction.
    /// </summary>
    public async Task<OperationResult> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _transactions.ExecuteAsync(async () =>
        {
            if (await _customers.GetByIdAsync(id, cancellationToken) is null)
                return OperationResult.Failure("مشتری دیگر وجود ندارد.");

            if (await _customers.HasMessageHistoryAsync(id, cancellationToken))
                return OperationResult.Failure(
                    "این مشتری سابقه پیام‌های ارسال‌شده دارد و قابل حذف نیست.");

            await _customers.DeleteAsync(id, cancellationToken);
            return OperationResult.Success();
        });
    }
}
