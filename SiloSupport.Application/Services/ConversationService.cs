using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;

namespace SiloSupport.Application.Services;

/// <summary>Use cases behind the customer conversations page.</summary>
public class ConversationService
{
    private readonly ICustomerRepository _customers;
    private readonly ICustomerGroupRepository _groups;
    private readonly ICustomerGroupedMessageRepository _messages;

    public ConversationService(
        ICustomerRepository customers,
        ICustomerGroupRepository groups,
        ICustomerGroupedMessageRepository messages)
    {
        _customers = customers;
        _groups = groups;
        _messages = messages;
    }

    /// <summary>
    /// Loads one customer's destination groups and only that customer's messages, grouped by group (most urgent
    /// group first, messages oldest first). Returns null when the customer does not exist.
    /// </summary>
    public async Task<CustomerConversationDto?> GetAsync(int customerId, CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetByIdAsync(customerId, cancellationToken);
        if (customer is null)
            return null;

        var groups = await _groups.GetByCustomerAsync(customerId, cancellationToken);
        var messages = await _messages.GetByCustomerAsync(customerId, cancellationToken);

        var byGroup = messages
            .GroupBy(m => m.GroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<GroupedMessageDto>)g.OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id).ToList());

        var groupDtos = groups
            .OrderByDescending(g => (int)g.Priority)
            .Select(g => new GroupConversationDto(
                g.Id,
                g.Name,
                g.Priority,
                byGroup.TryGetValue(g.Id, out var list) ? list : Array.Empty<GroupedMessageDto>()))
            .ToList();

        return new CustomerConversationDto(
            customer.Id,
            customer.Name,
            groupDtos,
            PriorityHelper.Missing(groups.Select(g => g.Priority)));
    }

    /// <summary>Marks a message of the given customer as resolved or unresolved and persists it.</summary>
    public async Task<OperationResult> SetResolvedAsync(int customerId, int messageId, bool isResolved, CancellationToken cancellationToken = default)
    {
        var updated = await _messages.SetResolvedAsync(customerId, messageId, isResolved, cancellationToken);
        return updated ? OperationResult.Success() : OperationResult.Failure("پیام برای این مشتری یافت نشد.");
    }
}
